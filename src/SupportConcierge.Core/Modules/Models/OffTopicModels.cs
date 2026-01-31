namespace SupportConcierge.Core.Modules.Models;

public sealed class OffTopicAssessment
{
    public bool OffTopic { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
}

