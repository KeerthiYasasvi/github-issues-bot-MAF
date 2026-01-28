using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Guardrails;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Apply guardrails (stop command, author gating, loop limit, etc.)
/// 
/// Responsibilities:
/// 1. Build allow list: issue author + users who used /diagnose
/// 2. Detect special commands (/stop, /diagnose)
/// 3. Validate command permissions (only allowed users)
/// 4. Enforce per-user loop limit (max 3 iterations per user)
/// 5. Check if user conversation is already finalized
/// </summary>
public sealed class GuardrailsExecutor : Executor<RunContext, RunContext>
{
    private const int MaxLoops = 3;

    public GuardrailsExecutor()
        : base("guardrails", ExecutorDefaults.Options, false)
    {
    }

    public override ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        Console.WriteLine("[MAF] Guardrails: Applying security and flow control checks");

        var issueAuthor = input.Issue?.User?.Login ?? string.Empty;
        var incomingCommentAuthor = input.IncomingComment?.User?.Login ?? string.Empty;

        // Get bot username from environment (check both possible values)
        var preferredBotUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";
        var actualBotUsername = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? preferredBotUsername;

        // CRITICAL FIX: Ignore comments from the bot itself (prevents infinite loop)
        if (input.EventName == "issue_comment" && 
            (incomingCommentAuthor == preferredBotUsername || incomingCommentAuthor == actualBotUsername))
        {
            Console.WriteLine($"[MAF] Guardrails: Comment from bot ({incomingCommentAuthor}). Ignoring to prevent loop.");
            input.ShouldStop = true;
            input.StopReason = "Bot comment - ignoring to prevent infinite loop";
            return new ValueTask<RunContext>(input);
        }

        // Determine the active participant
        if (input.EventName == "issue_comment" && !string.IsNullOrWhiteSpace(incomingCommentAuthor))
        {
            input.ActiveParticipant = incomingCommentAuthor;
        }
        else
        {
            input.ActiveParticipant = issueAuthor;
        }

        // Check for command parser
        var bodyText = (input.Issue?.Body ?? "") + " " + (input.IncomingComment?.Body ?? "");
        var commandInfo = CommandParser.Parse(bodyText);

        // Build allow list: issue author + users who have used /diagnose
        var allowedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { issueAuthor };
        if (input.State?.UserConversations != null)
        {
            foreach (var username in input.State.UserConversations.Keys)
            {
                allowedUsers.Add(username);
            }
        }

        // Handle /diagnose command - adds user to allow list
        if (commandInfo.HasDiagnoseCommand)
        {
            input.IsDiagnoseCommand = true;
            Console.WriteLine($"[MAF] Guardrails: /diagnose command from {input.ActiveParticipant} - adding to allow list");
            
            // Will be added to UserConversations in LoadStateExecutor
            allowedUsers.Add(input.ActiveParticipant);
        }

        // SECURITY: Author gating - only allowed users can interact
        if (input.EventName == "issue_comment" && !allowedUsers.Contains(incomingCommentAuthor))
        {
            Console.WriteLine($"[MAF] Guardrails: Comment from {incomingCommentAuthor} (not in allow list). Ignoring.");
            Console.WriteLine($"[MAF] Guardrails: Allowed users: {string.Join(", ", allowedUsers)}");
            Console.WriteLine($"[MAF] Guardrails: Hint: User must use /diagnose to join conversation");
            input.ShouldStop = true;
            input.StopReason = $"User {incomingCommentAuthor} not in allow list. Use /diagnose to join.";
            return new ValueTask<RunContext>(input);
        }

        if (commandInfo.HasStopCommand)
        {
            input.IsStopCommand = true;
            input.ShouldStop = true;
            input.StopReason = $"{input.ActiveParticipant} invoked /stop command";
            Console.WriteLine($"[MAF] Guardrails: /stop command from {input.ActiveParticipant} - marking their conversation as finalized");

            // Mark user's conversation as finalized
            if (input.State?.UserConversations != null && 
                input.State.UserConversations.TryGetValue(input.ActiveParticipant, out var userConv))
            {
                userConv.IsFinalized = true;
                userConv.FinalizedAt = DateTime.UtcNow;
            }
            return new ValueTask<RunContext>(input);
        }

        if (commandInfo.HasDiagnoseCommand)
        {
            // Already handled above - just log
            Console.WriteLine($"[MAF] Guardrails: /diagnose command from {input.ActiveParticipant} - will start fresh conversation");
            // Continue processing normally
        }

        // Check if this user's conversation is already finalized
        if (input.State?.UserConversations != null &&
            input.State.UserConversations.TryGetValue(input.ActiveParticipant, out var activeUserConv) &&
            activeUserConv.IsFinalized)
        {
            Console.WriteLine($"[MAF] Guardrails: {input.ActiveParticipant}'s conversation already finalized at {activeUserConv.FinalizedAt}");

            // Check for disagreement (allow regeneration if disagreement detected)
            if (input.EventName == "issue_comment" && input.IncomingComment != null)
            {
                if (DisagreementDetector.IsDisagreement(input.IncomingComment.Body))
                {
                    input.IsDisagreement = true;
                    Console.WriteLine($"[MAF] Guardrails: {input.ActiveParticipant} disagreement detected - allowing brief regeneration");
                    // Continue processing to handle disagreement
                    return new ValueTask<RunContext>(input);
                }
            }

            input.ShouldStop = true;
            input.StopReason = $"{input.ActiveParticipant}'s conversation already finalized";
            return new ValueTask<RunContext>(input);
        }

        // Enforce per-user loop limit
        var currentLoop = 0;
        if (input.State?.UserConversations != null &&
            input.State.UserConversations.TryGetValue(input.ActiveParticipant, out var userLoop))
        {
            currentLoop = userLoop.LoopCount;
        }

        if (currentLoop >= MaxLoops)
        {
            Console.WriteLine($"[MAF] Guardrails: {input.ActiveParticipant} max loops ({MaxLoops}) reached - will escalate");
            input.ShouldEscalate = true;
            return new ValueTask<RunContext>(input);
        }

        Console.WriteLine($"[MAF] Guardrails: All checks passed for {input.ActiveParticipant}. Loop {currentLoop}/{MaxLoops}");
        return new ValueTask<RunContext>(input);
    }
}
