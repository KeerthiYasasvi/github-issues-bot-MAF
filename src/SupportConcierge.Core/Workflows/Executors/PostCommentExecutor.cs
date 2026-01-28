using System.Text;
using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Post a response or follow-up comment to GitHub.
/// 
/// Responsibilities:
/// 1. Compose well-formatted comments
/// 2. Embed bot state in hidden HTML comment for persistence
/// 3. Include clear instructions for user interaction
/// 4. Track loop count for escalation decisions
/// </summary>
public sealed class PostCommentExecutor : Executor<RunContext, RunContext>
{
    private readonly IGitHubTool _gitHub;
    private readonly StateStoreTool _stateTool;

    public PostCommentExecutor(IGitHubTool gitHub)
        : base("post_comment", ExecutorDefaults.Options, false)
    {
        _gitHub = gitHub;
        _stateTool = new StateStoreTool();
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

        // Embed state in hidden HTML comment for persistence across invocations
        if (input.State != null)
        {
            // Update state before embedding
            input.State.LastUpdated = DateTime.UtcNow;
            
            body = _stateTool.EmbedState(body, input.State);
            
            // Log active user's loop count
            var userLoop = input.ActiveUserConversation?.LoopCount ?? 0;
            var userCount = input.State.UserConversations?.Count ?? 0;
            Console.WriteLine($"[MAF] PostComment: Embedded state ({input.ActiveParticipant} Loop={userLoop}, {userCount} users, Category={input.State.Category})");
        }

        var comment = await _gitHub.PostCommentAsync(owner, repo, issueNumber, body, ct);
        if (comment == null)
        {
            Console.WriteLine("[MAF] PostComment: Skipped (dry-run or write-mode disabled)." );
        }
        else
        {
            Console.WriteLine($"[MAF] PostComment: Posted comment id {comment.Id}.");
            
            // Track that a comment was successfully posted - update last update time
            if (input.ExecutionState != null)
            {
                input.ExecutionState.LastUpdate = DateTime.UtcNow;
            }
        }

        return input;
    }

    private static string ComposeComment(RunContext input)
    {
        var sb = new StringBuilder();
        var author = input.Issue?.User?.Login;
        
        // Mention author
        if (!string.IsNullOrWhiteSpace(author))
        {
            sb.AppendLine($"@{author}");
            sb.AppendLine();
        }

        if (input.ShouldStop && !string.IsNullOrWhiteSpace(input.StopReason))
        {
            // Handle /stop command
            sb.AppendLine($"You've opted out with `/stop`. I won't ask further questions on this issue.");
            sb.AppendLine();
            sb.AppendLine("If you need to restart, comment with `/diagnose`.");
            return sb.ToString().Trim();
        }

        if (input.ShouldAskFollowUps)
        {
            // Follow-up questions (formatted like previous project)
            var loopCount = input.State?.LoopCount ?? input.CurrentLoopCount ?? 1;
            var maxLoops = input.ExecutionState?.TotalUserLoops ?? 3;
            
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

            // Add helpful instructions
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("**How to interact with me:**");
            sb.AppendLine("- Reply directly to this comment with your answers");
            sb.AppendLine("- If you need to restart analysis, comment with `/diagnose`");
            sb.AppendLine("- If you want me to stop, comment with `/stop`");
            sb.AppendLine();
            sb.AppendLine($"_Loop {loopCount} of {maxLoops}. I'll escalate to maintainer after {maxLoops} attempts if issue remains unclear._");

            return sb.ToString().Trim();
        }

        if (input.ShouldEscalate)
        {
            // Escalation message - now accurate based on ExecutionState
            var execState = input.ExecutionState;
            var loopNum = execState?.LoopNumber ?? input.CurrentLoopCount ?? 1;
            var isUserExhausted = execState?.IsUserExhausted ?? (loopNum > 3);
            
            sb.AppendLine("I've attempted to analyze this issue but need human review to proceed effectively.");
            sb.AppendLine();
            sb.AppendLine("**Escalating to maintainer review** for expert assessment.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("**What happened:**");
            
            if (isUserExhausted)
            {
                sb.AppendLine($"- ✅ I completed {execState?.TotalUserLoops ?? 3} loops of interaction");
                sb.AppendLine("- ✅ You provided responses to my clarifying questions");
                sb.AppendLine("- ⚠️ Even with your answers, I cannot provide a confident automated response");
                sb.AppendLine();
                sb.AppendLine("Your issue may require:");
                sb.AppendLine("- Expert knowledge of the codebase");
                sb.AppendLine("- Access to internal systems or logs");
                sb.AppendLine("- Complex troubleshooting beyond automated analysis");
            }
            else
            {
                sb.AppendLine("- I analyzed the issue details");
                sb.AppendLine("- I need additional context to provide an accurate response");
                sb.AppendLine();
                sb.AppendLine("You can help by:");
                sb.AppendLine("- Adding more details in a follow-up comment");
                sb.AppendLine("- Sharing relevant error messages or logs");
                sb.AppendLine("- Describing reproduction steps in detail");
            }
            
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("**Next steps:**");
            sb.AppendLine("- A maintainer will review your issue shortly");
            sb.AppendLine("- If you have additional details, please share them in a comment below");
            sb.AppendLine("- Use `/diagnose` to restart fresh analysis if needed");
            
            return sb.ToString().Trim();
        }

        // Brief/solution format (when issue is actionable)
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
                sb.AppendLine("**Recommended next steps:**");
                foreach (var step in brief.NextSteps)
                {
                    sb.AppendLine($"- {step}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("This issue has been analyzed and categorized. Please let me know if this assessment is correct or if you'd like me to reconsider.");

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
