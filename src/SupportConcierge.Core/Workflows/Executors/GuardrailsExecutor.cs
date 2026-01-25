using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Apply guardrails (stop command, author gating, etc.)
/// </summary>
public sealed class GuardrailsExecutor : Executor<RunContext, RunContext>
{
    public GuardrailsExecutor()
        : base("guardrails", ExecutorDefaults.Options, false)
    {
    }

    public override ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        // Check for /stop command in issue or comment
        var bodyText = (input.Issue?.Body ?? "") + " " + (input.IncomingComment?.Body ?? "");
        if (bodyText.Contains("/stop", StringComparison.OrdinalIgnoreCase))
        {
            input.ShouldStop = true;
            Console.WriteLine("[MAF] Guardrails: /stop command detected");
        }

        return new ValueTask<RunContext>(input);
    }
}
