namespace SupportConcierge.Core.Models;

public sealed class ResearchDirective
{
    public bool ShouldResearch { get; set; }
    public List<string> AllowedTools { get; set; } = new();
    public bool AllowWebSearch { get; set; }
    public string QueryQuality { get; set; } = "low";
    public string RecommendedQuery { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}
