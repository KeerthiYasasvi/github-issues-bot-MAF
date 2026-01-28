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

        // Initialize ExecutionState if not present
        if (input.ExecutionState == null)
        {
            input.ExecutionState = new ExecutionState();
        }

        // Track loop count
        input.CurrentLoopCount = (input.CurrentLoopCount ?? 0) + 1;
        Console.WriteLine($"[MAF] Orchestrator: Loop {input.CurrentLoopCount}/3");

        // Update ExecutionState loop tracking
        input.ExecutionState.LoopNumber = input.CurrentLoopCount.Value;

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
            input.ExecutionState.LoopActionTaken = "provide_final_response";
        }
        else if (decision.Action == "escalate")
        {
            input.ShouldEscalate = true;
            input.ExecutionState.LoopActionTaken = "escalate";
            input.ExecutionState.IsUserExhausted = input.CurrentLoopCount > input.ExecutionState.TotalUserLoops;
        }
        else if (decision.Action == "respond_with_questions")
        {
            input.ShouldAskFollowUps = true;
            if (input.CurrentLoopCount == 1)
            {
                input.ExecutionState.LoopActionTaken = "ask_clarifying_questions";
            }
            else
            {
                input.ExecutionState.LoopActionTaken = "ask_more_questions";
            }
        }
        else if (decision.Action == "respond")
        {
            input.ShouldFinalize = true;
            input.ExecutionState.LoopActionTaken = "provide_response";
        }

        return input;
    }
}
