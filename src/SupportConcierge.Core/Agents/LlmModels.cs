namespace SupportConcierge.Core.Agents;

public sealed class LlmMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class LlmRequest
{
    public List<LlmMessage> Messages { get; set; } = new();
    public string? JsonSchema { get; set; }
    public string SchemaName { get; set; } = "response";
    public double Temperature { get; set; }
}

public sealed class LlmResponse
{
    public string Content { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public double LatencyMs { get; set; }
    public bool IsSuccess { get; set; }
    public string RawResponse { get; set; } = string.Empty;
}

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
