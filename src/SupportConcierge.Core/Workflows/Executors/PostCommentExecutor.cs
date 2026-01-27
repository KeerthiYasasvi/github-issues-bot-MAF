using System.Text;
using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Post a response or follow-up comment to GitHub.
/// </summary>
public sealed class PostCommentExecutor : Executor<RunContext, RunContext>
{
    private readonly IGitHubTool _gitHub;

    public PostCommentExecutor(IGitHubTool gitHub)
        : base("post_comment", ExecutorDefaults.Options, false)
    {
        _gitHub = gitHub;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        var owner = input.Repository?.Owner?.Login ?? string.Empty;
        var repo = input.Repository?.Name ?? string.Empty;
        var issueNumber = input.Issue?.Number ?? 0;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || issueNumber <= 0)
        {
            Console.WriteLine("[MAF] PostComment: Missing repository or issue information.");
            return input;
        }

        var body = ComposeComment(input);
        if (string.IsNullOrWhiteSpace(body))
        {
            Console.WriteLine("[MAF] PostComment: No content generated; skipping.");
            return input;
        }

        var comment = await _gitHub.PostCommentAsync(owner, repo, issueNumber, body, ct);
        if (comment == null)
        {
            Console.WriteLine("[MAF] PostComment: Skipped (dry-run or write-mode disabled)." );
        }
        else
        {
            Console.WriteLine($"[MAF] PostComment: Posted comment id {comment.Id}.");
        }

        return input;
    }

    private static string ComposeComment(RunContext input)
    {
        var sb = new StringBuilder();
        var author = input.Issue?.User?.Login;
        if (!string.IsNullOrWhiteSpace(author))
        {
            sb.AppendLine($"@{author}");
            sb.AppendLine();
        }

        if (input.ShouldAskFollowUps)
        {
            sb.AppendLine("I need a bit more information to help move this forward:");
            sb.AppendLine();

            var questions = input.FollowUpQuestions.Select(q => q.Question)
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .ToList();

            if (questions.Count == 0 && input.ResponseResult?.FollowUps != null)
            {
                questions.AddRange(input.ResponseResult.FollowUps.Where(q => !string.IsNullOrWhiteSpace(q)));
            }

            if (questions.Count == 0)
            {
                questions.Add("Please share the exact error message and any relevant logs or stack traces.");
                questions.Add("What OS and versions are you using (runtime/build tool)?");
                questions.Add("What steps lead to the failure?");
            }

            for (var i = 0; i < questions.Count && i < 3; i++)
            {
                sb.AppendLine($"{i + 1}. {questions[i]}");
            }

            return sb.ToString().Trim();
        }

        if (input.ShouldEscalate)
        {
            sb.AppendLine("Escalating for human review. The automated checks reached the loop limit.");
            sb.AppendLine();
        }

        var brief = input.Brief;
        if (brief != null)
        {
            sb.AppendLine($"**Summary:** {brief.Summary}");
            sb.AppendLine();

            if (brief.KeyEvidence.Count > 0)
            {
                sb.AppendLine("**Key evidence:**");
                foreach (var evidence in brief.KeyEvidence.Take(3))
                {
                    sb.AppendLine($"- {evidence}");
                }
                sb.AppendLine();
            }

            if (brief.NextSteps.Count > 0)
            {
                sb.AppendLine("**Next steps:**");
                foreach (var step in brief.NextSteps)
                {
                    sb.AppendLine($"- {step}");
                }
            }

            return sb.ToString().Trim();
        }

        var response = input.ResponseResult?.Brief;
        if (response != null)
        {
            sb.AppendLine($"**Summary:** {response.Summary}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(response.Solution))
            {
                sb.AppendLine($"**Suggested fix:** {response.Solution}");
                sb.AppendLine();
            }
            if (response.NextSteps.Count > 0)
            {
                sb.AppendLine("**Next steps:**");
                foreach (var step in response.NextSteps)
                {
                    sb.AppendLine($"- {step}");
                }
            }

            return sb.ToString().Trim();
        }

        return string.Empty;
    }
}
