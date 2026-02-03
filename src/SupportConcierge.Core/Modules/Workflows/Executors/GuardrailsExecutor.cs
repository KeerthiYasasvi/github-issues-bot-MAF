using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Modules.Guardrails;
using SupportConcierge.Core.Modules.Models;

namespace SupportConcierge.Core.Modules.Workflows.Executors;

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
    private const int MaxLoops = 4;

    public GuardrailsExecutor()
        : base("guardrails", ExecutorDefaults.Options, false)
    {
    }

    public override ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        Console.WriteLine("[MAF] Guardrails: Applying security and flow control checks");

        var issueAuthor = input.Issue?.User?.Login ?? string.Empty;
        var incomingCommentAuthor = input.IncomingComment?.User?.Login ?? string.Empty;

        // Get bot username from environment variable
        var botUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";

        // CRITICAL FIX: Ignore comments from the bot itself (prevents infinite loop)
        if (input.EventName == "issue_comment" && incomingCommentAuthor == botUsername)
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

        // Check for commands - CRITICAL: Only parse the incoming comment, NOT issue body or previous comments
        var commandText = input.EventName == "issue_comment" 
            ? (input.IncomingComment?.Body ?? "")  // For comments, check only the comment
            : (input.Issue?.Body ?? "");            // For issue open/edit, check issue body
        var commandInfo = CommandParser.Parse(commandText);

        // Check for /diagnose command FIRST - this allows new users to join
        if (commandInfo.HasDiagnoseCommand)
        {
            input.IsDiagnoseCommand = true;
            Console.WriteLine($"[MAF] Guardrails: /diagnose command from {input.ActiveParticipant} - user will be added to conversation");
            // Continue processing - LoadStateExecutor will add to UserConversations
            // Don't return early, let the flow continue
        }

        // Build allow list: issue author + users who have used /diagnose (in UserConversations)
        var allowedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { issueAuthor };
        if (input.State?.UserConversations != null)
        {
            foreach (var username in input.State.UserConversations.Keys)
            {
                allowedUsers.Add(username);
            }
        }
        
        // If user just used /diagnose, they're now allowed (add them immediately)
        if (commandInfo.HasDiagnoseCommand)
        {
            allowedUsers.Add(input.ActiveParticipant);
        }

        // SECURITY: Author gating - only allowed users can interact (unless they just used /diagnose)
        if (input.EventName == "issue_comment" && !allowedUsers.Contains(incomingCommentAuthor))
        {
            Console.WriteLine($"[MAF] Guardrails: Comment from {incomingCommentAuthor} (not in allow list). Silently ignoring.");
            Console.WriteLine($"[MAF] Guardrails: Allowed users: {string.Join(", ", allowedUsers)}");
            Console.WriteLine($"[MAF] Guardrails: Hint: User can use /diagnose to join conversation");
            input.ShouldStop = true;
            input.StopReason = $"User {incomingCommentAuthor} not in allow list";
            return new ValueTask<RunContext>(input);
        }

        // Off-topic spam block: after 2 off-topic strikes, ignore further comments (even /diagnose)
        if (input.State?.UserConversations != null &&
            input.State.UserConversations.TryGetValue(input.ActiveParticipant, out var offTopicConv) &&
            (offTopicConv.IsOffTopicBlocked || offTopicConv.OffTopicStrikeCount >= 2))
        {
            Console.WriteLine($"[MAF] Guardrails: {input.ActiveParticipant} is off-topic blocked (strikes={offTopicConv.OffTopicStrikeCount}). Ignoring.");
            input.ShouldStop = true;
            input.StopReason = $"{input.ActiveParticipant} off-topic blocked";
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

