namespace SupportConcierge.Core.Modules.Evals;

public interface IAgentEvalSink
{
    Task WriteAsync(AgentEvalRecord record, CancellationToken cancellationToken = default);
}

