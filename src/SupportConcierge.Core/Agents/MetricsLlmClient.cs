namespace SupportConcierge.Core.Agents;

public sealed class MetricsLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly Action<LlmResponse> _onResponse;

    public MetricsLlmClient(ILlmClient inner, Action<LlmResponse> onResponse)
    {
        _inner = inner;
        _onResponse = onResponse;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _inner.CompleteAsync(request, cancellationToken);
        if (response.IsSuccess)
        {
            _onResponse(response);
        }

        return response;
    }
}
