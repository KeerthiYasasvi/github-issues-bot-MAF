namespace SupportConcierge.Core.Evals;

public interface IAgentEvalSink
{
    Task WriteAsync(AgentEvalRecord record, CancellationToken cancellationToken = default);
}
