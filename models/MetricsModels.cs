namespace SupportConcierge.Models;

public sealed class MetricsRecord
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public Dictionary<string, StepMetrics> Steps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TokenUsageSummary TokenUsage { get; set; } = new();
    public Dictionary<string, string> Decisions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; set; } = new();
}

public sealed class StepMetrics
{
    public string Name { get; set; } = string.Empty;
    public double DurationMs { get; set; }
}

public sealed class TokenUsageSummary
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int Calls { get; set; }
    public double TotalLatencyMs { get; set; }
}
