using System.Text;
using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Core.Modules.Workflows.Executors;

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

        // SECURITY: Skip posting if user not in allow list (silent ignore)
        if (input.ShouldStop && input.StopReason != null && input.StopReason.Contains("not in allow list"))
        {
            Console.WriteLine($"[MAF] PostComment: Skipping comment - user not in allow list");
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
            
            // Log active user's loop count with detailed user state
            var userLoop = input.ActiveUserConversation?.LoopCount ?? 0;
            var userCount = input.State.UserConversations?.Count ?? 0;
            Console.WriteLine($"[MAF] PostComment: Embedding state for {input.ActiveParticipant}");
            Console.WriteLine($"[MAF] PostComment:   - Active user LoopCount={userLoop}");
            Console.WriteLine($"[MAF] PostComment:   - Total users in state={userCount}");
            Console.WriteLine($"[MAF] PostComment:   - Category={input.State.Category}");
            foreach (var kvp in input.State.UserConversations)
            {
                Console.WriteLine($"[MAF] PostComment:   - User '{kvp.Key}': LoopCount={kvp.Value.LoopCount}, IsExhausted={kvp.Value.IsExhausted}, IsFinalized={kvp.Value.IsFinalized}");
            }
            
            // DEBUG: Show last 300 chars of body to verify state marker is present
            var bodyEnd = body.Length > 300 ? body.Substring(body.Length - 300) : body;
            Console.WriteLine($"[MAF] PostComment: DEBUG - Last 300 chars of body to post:");
            Console.WriteLine(bodyEnd);
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
            input.CommentPosted = true;
            input.PostedCommentCount += 1;
            input.LastPostedCommentId = comment.Id;
        }

        return input;
    }

    private static string ComposeComment(RunContext input)
    {
        var sb = new StringBuilder();
        var activeUser = input.ActiveParticipant ?? input.Issue?.User?.Login;
        
        // Mention the active participant (actual commenter), not always the issue author
        if (!string.IsNullOrWhiteSpace(activeUser))
        {
            sb.AppendLine($"@{activeUser}");
            sb.AppendLine();
        }

        if (input.ShouldRedirectOffTopic && input.OffTopicAssessment != null)
        {
            var issueTitle = input.Issue?.Title ?? "this issue";
            var reason = input.OffTopicAssessment.Reason;
            var suggested = input.OffTopicAssessment.SuggestedAction;
            var isBlocked = input.ActiveUserConversation?.IsOffTopicBlocked == true;
            var isIssueEvent = input.EventName == "issues";
            var subject = isIssueEvent ? "issue" : "comment";

            sb.AppendLine($"It looks like your {subject} is about something different from this thread ({issueTitle}).");
            if (!string.IsNullOrWhiteSpace(reason))
            {
                sb.AppendLine();
                sb.AppendLine($"**Why I think this is off-topic:** {reason}");
            }

            sb.AppendLine();
            sb.AppendLine("To keep this issue focused, please open a new issue for that topic or reply in the correct thread.");
            if (!string.IsNullOrWhiteSpace(suggested))
            {
                sb.AppendLine();
                sb.AppendLine($"**Suggested action:** {suggested}");
            }

            sb.AppendLine();
            sb.AppendLine("If you intended to discuss this issue, please clarify how your content relates to the current topic.");
            if (isBlocked)
            {
                sb.AppendLine();
                sb.AppendLine("**Note:** You've gone off-topic multiple times in this thread, so I won't respond to further comments here.");
            }
            return sb.ToString().Trim();
        }

        if (input.ShouldStop && input.IsStopCommand)
        {
            // Handle explicit /stop command from user
            sb.AppendLine($"You've used `/stop`. I won't ask you any more questions.");
            sb.AppendLine();
            sb.AppendLine("Note: Other users can still interact with me by commenting with `/diagnose`.");
            sb.AppendLine();
            sb.AppendLine("If you want to restart your conversation, comment with `/diagnose`.");
            return sb.ToString().Trim();
        }
        
        // If ShouldStop but NOT IsStopCommand, it's finalized or other internal reason
        if (input.ShouldStop)
        {
            if (input.StopReason != null && input.StopReason.Contains("diagnose reset limit", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("You've already used `/diagnose` once in this issue, so I can't restart the conversation again.");
                sb.AppendLine();
                sb.AppendLine("If you have new details, please add them to your existing thread instead.");
                return sb.ToString().Trim();
            }
            Console.WriteLine($"[MAF] PostComment: ShouldStop=true but no comment needed (reason: {input.StopReason})");
            return string.Empty;  // Return empty to skip posting
        }

        if (input.ShouldAskFollowUps)
        {
            // Follow-up questions (formatted like previous project)
            var loopCount = input.ActiveUserConversation?.LoopCount ?? input.CurrentLoopCount ?? 1;
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
                // Category-aware fallback questions based on triage classification
                var category = input.CategoryDecision?.Category?.ToLower() ?? "unknown";
                
                if (category.Contains("documentation"))
                {
                    questions.Add("Which documentation file or section needs to be updated?");
                    questions.Add("What is currently incorrect or missing in the documentation?");
                    questions.Add("What should the documentation say instead?");
                }
                else if (category.Contains("feature"))
                {
                    questions.Add("What problem would this feature solve for you?");
                    questions.Add("How do you envision this feature working?");
                    questions.Add("Have you considered any alternative approaches?");
                }
                else if (category.Contains("configuration") || category.Contains("config"))
                {
                    questions.Add("Which configuration file or setting are you trying to modify?");
                    questions.Add("What configuration have you tried so far?");
                    questions.Add("What behavior do you expect vs. what actually happens?");
                }
                else if (category.Contains("build"))
                {
                    questions.Add("What build tool and version are you using?");
                    questions.Add("Please share the build output and any error messages.");
                    questions.Add("Have you verified all build dependencies are installed?");
                }
                else if (category.Contains("dependency"))
                {
                    questions.Add("Which package manager are you using (npm, pip, maven, etc.)?");
                    questions.Add("Please share your dependency lock file (package-lock.json, requirements.txt, etc.).");
                    questions.Add("Which packages are conflicting?");
                }
                else if (category.Contains("environment"))
                {
                    questions.Add("Which setup step is failing?");
                    questions.Add("What operating system and prerequisites do you have installed?");
                    questions.Add("Please share any setup logs or error output.");
                }
                else // runtime_error, bug_report, or unknown
                {
                    questions.Add("Please share the exact error message and any relevant logs or stack traces.");
                    questions.Add("What OS and versions are you using (runtime/build tool)?");
                    questions.Add("What steps lead to the failure?");
                }
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
            sb.AppendLine($"_Loop {loopCount} of {maxLoops}. I'll escalate to maintainer after {maxLoops} loops if issue remains unclear._");

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
                // Show 3 as max loops to user (since loop 4 is just the escalation trigger)
                var displayLoops = Math.Min(execState?.TotalUserLoops ?? 3, 3);
                sb.AppendLine($"- ✅ I completed {displayLoops} loops of interaction (up to 9 questions)");
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

            var escalationSummary = BuildEscalationSummary(input);
            if (!string.IsNullOrWhiteSpace(escalationSummary))
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("**Escalation summary:**");
                sb.AppendLine(escalationSummary);
            }
            
            return sb.ToString().Trim();
        }

        // Check for self-resolution confirmation
        if (input.ShouldFinalize && input.ExecutionState?.LoopActionTaken == "confirm_self_resolution")
        {
            sb.AppendLine("It sounds like you've identified the solution! 🎉");
            sb.AppendLine();
            sb.AppendLine("If the fix works, feel free to close this issue. If you run into other problems, just comment here and I'll help investigate.");
            sb.AppendLine();
            sb.AppendLine("Good luck!");
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

            var escalationSummary = BuildEscalationSummary(input);
            if (!string.IsNullOrWhiteSpace(escalationSummary))
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("**Collected info & gaps:**");
                sb.AppendLine(escalationSummary);
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

    private static string BuildEscalationSummary(RunContext input)
    {
        var sb = new StringBuilder();

        var collected = new List<string>();
        if (input.TriageResult?.ExtractedDetails?.Count > 0)
        {
            collected.AddRange(input.TriageResult.ExtractedDetails
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Take(6)
                .Select(kv => $"{kv.Key}: {kv.Value}"));
        }

        if (input.CasePacket?.Fields?.Count > 0)
        {
            collected.AddRange(input.CasePacket.Fields
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Take(6)
                .Select(kv => $"{kv.Key}: {kv.Value}"));
        }

        if (collected.Count > 0)
        {
            sb.AppendLine("**Collected details:**");
            foreach (var item in collected.Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
            {
                sb.AppendLine($"- {item}");
            }
        }

        var missing = input.Scoring?.MissingFields
            ?.Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList() ?? new List<string>();

        if (missing.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.AppendLine("**Missing info:**");
            foreach (var item in missing)
            {
                sb.AppendLine($"- {item}");
            }
        }

        return sb.ToString().Trim();
    }
}

