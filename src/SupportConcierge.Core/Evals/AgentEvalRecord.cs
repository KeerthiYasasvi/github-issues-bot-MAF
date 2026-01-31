namespace SupportConcierge.Core.Evals;

public sealed class AgentEvalRecord
{
    public string RunId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public long DurationMs { get; set; }
    public string InputHash { get; set; } = string.Empty;
    public string OutputHash { get; set; } = string.Empty;
    public string? InputSnippet { get; set; }
    public string? OutputSnippet { get; set; }
    public bool SecretsRedacted { get; set; }
    public string[] ToolAllowList { get; set; } = Array.Empty<string>();
    public string[] ToolsUsed { get; set; } = Array.Empty<string>();
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public double LatencyMs { get; set; }
    public Judgement Judgement { get; set; } = new();
}
