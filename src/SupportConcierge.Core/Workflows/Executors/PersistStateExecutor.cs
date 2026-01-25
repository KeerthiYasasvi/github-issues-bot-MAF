using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Persist state (final step)
/// </summary>
public sealed class PersistStateExecutor : Executor<RunContext, RunContext>
{
    public PersistStateExecutor()
        : base("persist_state", ExecutorDefaults.Options, false)
    {
    }

    public override ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        Console.WriteLine("[MAF] PersistState: Saving workflow state");

        // Mark as finalized if appropriate
        if (input.ShouldFinalize || input.ShouldEscalate || input.ShouldStop)
        {
            Console.WriteLine("[MAF] PersistState: Workflow completed");
        }

        return new ValueTask<RunContext>(input);
    }
}
