namespace SupportConcierge.Core.Modules.Evals;

public sealed class RubricDefinition
{
    public string RubricId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public double ThresholdScore { get; set; } = 7;
    public List<RubricItem> Items { get; set; } = new();
}

public sealed class RubricItem
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double MaxPoints { get; set; } = 2;
    public string Type { get; set; } = "rule"; // rule | judge
    public string? RuleId { get; set; }
    public string? Instructions { get; set; }
}

