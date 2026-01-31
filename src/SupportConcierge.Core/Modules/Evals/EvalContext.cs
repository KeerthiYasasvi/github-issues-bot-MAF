using SupportConcierge.Core.Modules.Models;

namespace SupportConcierge.Core.Modules.Evals;

public sealed class EvalContext
{
    public string RunId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string[] ToolAllowList { get; set; } = Array.Empty<string>();
    public string[] ToolsUsed { get; set; } = Array.Empty<string>();
    public List<string> MissingFields { get; set; } = new();
    public List<FollowUpQuestion> FollowUpQuestions { get; set; } = new();
    public string InputText { get; set; } = string.Empty;
    public string OutputText { get; set; } = string.Empty;
    public string? ExpectedJsonSchema { get; set; }
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public double LatencyMs { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
}

