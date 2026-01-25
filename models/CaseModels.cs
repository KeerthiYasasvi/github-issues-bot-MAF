namespace SupportConcierge.Models;

public sealed class CasePacket
{
    public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeterministicFields { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FollowUpQuestion
{
    public string Field { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string WhyNeeded { get; set; } = string.Empty;
}

public sealed class FollowUpResponse
{
    public List<FollowUpQuestion> Questions { get; set; } = new();
}

public sealed class EngineerBrief
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Symptoms { get; set; } = new();
    public List<string> ReproSteps { get; set; } = new();
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> KeyEvidence { get; set; } = new();
    public List<string> NextSteps { get; set; } = new();
    public List<string> ValidationConfirmations { get; set; } = new();
    public List<DuplicateReference> PossibleDuplicates { get; set; } = new();
}

public sealed class DuplicateReference
{
    public int IssueNumber { get; set; }
    public string SimilarityReason { get; set; } = string.Empty;
}

public sealed class ScoringResult
{
    public string Category { get; set; } = string.Empty;
    public int Score { get; set; }
    public int Threshold { get; set; }
    public bool IsActionable { get; set; }
    public List<string> MissingFields { get; } = new();
    public List<string> InvalidFields { get; } = new();
    public List<string> Issues { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class CategoryDecision
{
    public string Category { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}
