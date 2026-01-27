using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Guardrails;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Apply guardrails (stop command, author gating, loop limit, etc.)
/// 
/// Responsibilities:
/// 1. Enforce author-only interactions (only issue author can interact with bot)
/// 2. Detect special commands (/stop, /diagnose)
/// 3. Validate command permissions (only author can use these)
/// 4. Enforce loop limit (max 3 iterations before escalation)
/// 5. Check if issue is already finalized
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

        // Determine the active participant
        if (input.EventName == "issue_comment" && !string.IsNullOrWhiteSpace(incomingCommentAuthor))
        {
            input.ActiveParticipant = incomingCommentAuthor;
        }
        else
        {
            input.ActiveParticipant = issueAuthor;
        }

        // SECURITY: Author gating - only the issue author can interact with bot
        if (input.EventName == "issue_comment" && incomingCommentAuthor != issueAuthor)
        {
            Console.WriteLine($"[MAF] Guardrails: Comment from {incomingCommentAuthor} (not author). Ignoring.");
            input.ShouldStop = true;
            input.StopReason = "Only issue author can interact with bot";
            return new ValueTask<RunContext>(input);
        }

        // Check for command parser
        var bodyText = (input.Issue?.Body ?? "") + " " + (input.IncomingComment?.Body ?? "");
        var commandInfo = CommandParser.Parse(bodyText);

        if (commandInfo.HasStopCommand)
        {
            input.IsStopCommand = true;
            input.ShouldStop = true;
            input.StopReason = "User invoked /stop command";
            Console.WriteLine("[MAF] Guardrails: /stop command detected - marking as finalized");

            // Mark state as finalized if we have one
            if (input.State != null)
            {
                input.State.IsFinalized = true;
                input.State.FinalizedAt = DateTime.UtcNow;
            }
            return new ValueTask<RunContext>(input);
        }

        if (commandInfo.HasDiagnoseCommand)
        {
            input.IsDiagnoseCommand = true;
            Console.WriteLine("[MAF] Guardrails: /diagnose command detected - resetting state for fresh analysis");

            // Reset state to force fresh analysis
            if (input.State != null)
            {
                input.State.LoopCount = 0;
                input.State.AskedFields.Clear();
                input.State.IsFinalized = false;
                input.State.FinalizedAt = null;
            }
            // Continue processing normally
        }

        // Check if already finalized (state from previous comment)
        if (input.State?.IsFinalized ?? false)
        {
            Console.WriteLine($"[MAF] Guardrails: Issue already finalized at {input.State.FinalizedAt}");

            // Check for disagreement (Scenario 7: allow regeneration if disagreement detected)
            if (input.EventName == "issue_comment" && input.IncomingComment != null)
            {
                if (DisagreementDetector.IsDisagreement(input.IncomingComment.Body))
                {
                    input.IsDisagreement = true;
                    Console.WriteLine("[MAF] Guardrails: User disagreement detected - allowing brief regeneration");
                    // Continue processing to handle disagreement
                    return new ValueTask<RunContext>(input);
                }
            }

            input.ShouldStop = true;
            input.StopReason = "Issue already finalized";
            return new ValueTask<RunContext>(input);
        }

        // Enforce loop limit
        var currentLoop = input.State?.LoopCount ?? 0;
        if (currentLoop >= MaxLoops)
        {
            Console.WriteLine($"[MAF] Guardrails: Max loops ({MaxLoops}) reached - will escalate");
            input.ShouldEscalate = true;
            return new ValueTask<RunContext>(input);
        }

        Console.WriteLine($"[MAF] Guardrails: All checks passed. Loop {currentLoop}/{MaxLoops}, Author={issueAuthor}");
        return new ValueTask<RunContext>(input);
    }
}
