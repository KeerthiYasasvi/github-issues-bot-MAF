namespace SupportConcierge.Cli.Evals;

public sealed class EvalConfig
{
    public int MinFollowUpScore { get; set; } = 3;
    public int MinBriefScore { get; set; } = 3;
    public int MinCompletenessScore { get; set; } = 50;
    public double MaxAverageLatencyMs { get; set; } = 6000;
    public int MaxAverageTokens { get; set; } = 2000;
    public bool FailOnHallucinations { get; set; } = true;
}
