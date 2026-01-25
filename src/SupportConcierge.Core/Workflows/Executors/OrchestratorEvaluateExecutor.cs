using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Orchestrator evaluates progress and decides next action
/// </summary>
public sealed class OrchestratorEvaluateExecutor : Executor<RunContext, RunContext>
{
    private readonly OrchestratorAgent _orchestrator;

    public OrchestratorEvaluateExecutor(OrchestratorAgent orchestrator)
        : base("orchestrator_evaluate", ExecutorDefaults.Options, false)
    {
        _orchestrator = orchestrator;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        Console.WriteLine("[MAF] Orchestrator: Evaluating progress and deciding next action");

        // Initialize decisions list if not present
        if (input.Decisions == null)
        {
            input.Decisions = new List<OrchestratorDecision>();
        }

        // Track loop count
        input.CurrentLoopCount = (input.CurrentLoopCount ?? 0) + 1;
        Console.WriteLine($"[MAF] Orchestrator: Loop {input.CurrentLoopCount}/3");

        // Create or update plan
        if (input.Plan == null)
        {
            input.Plan = await _orchestrator.UnderstandTaskAsync(input, ct);
            Console.WriteLine($"[MAF] Orchestrator: Plan created - {input.Plan.ProblemSummary}");
        }

        // Evaluate progress
        var decision = await _orchestrator.EvaluateProgressAsync(input, input.Plan, input.CurrentLoopCount.Value, input.Decisions, ct);
        input.Decisions.Add(decision);

        Console.WriteLine($"[MAF] Orchestrator: Decision = {decision.Action} (confidence: {decision.ConfidenceScore:F2})");
        Console.WriteLine($"[MAF] Orchestrator: Reasoning - {decision.Reasoning}");

        // Set flags based on decision
        if (decision.Action == "finalize")
        {
            input.ShouldFinalize = true;
        }
        else if (decision.Action == "escalate")
        {
            input.ShouldEscalate = true;
        }
        else if (decision.Action == "follow_up")
        {
            input.ShouldAskFollowUps = true;
        }

        return input;
    }
}
