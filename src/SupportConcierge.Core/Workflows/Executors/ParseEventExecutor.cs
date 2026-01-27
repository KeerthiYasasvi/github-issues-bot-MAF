using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Parse GitHub event and initialize RunContext
/// </summary>
public sealed class ParseEventExecutor : Executor<EventInput, RunContext>
{
    public ParseEventExecutor()
        : base("parse_event", ExecutorDefaults.Options, false)
    {
    }

    public override ValueTask<RunContext> HandleAsync(EventInput input, IWorkflowContext context, CancellationToken ct = default)
    {
        var runContext = new RunContext
        {
            EventName = input.EventName,
            Issue = input.Issue,
            Repository = input.Repository,
            IncomingComment = input.Comment,
            DryRun = ParseBool(Environment.GetEnvironmentVariable("SUPPORTBOT_DRY_RUN")),
            WriteMode = ParseBool(Environment.GetEnvironmentVariable("SUPPORTBOT_WRITE_MODE"))
        };

        Console.WriteLine($"[MAF] ParseEvent: Issue #{runContext.Issue.Number}: {runContext.Issue.Title}");
        return new ValueTask<RunContext>(runContext);
    }

    private static bool ParseBool(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
