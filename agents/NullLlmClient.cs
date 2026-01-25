namespace SupportConcierge.Agents;

public sealed class NullLlmClient : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LlmResponse
        {
            IsSuccess = true,
            Content = "{}",
            RawResponse = "{}",
            PromptTokens = 0,
            CompletionTokens = 0,
            TotalTokens = 0,
            LatencyMs = 0
        });
    }
}
