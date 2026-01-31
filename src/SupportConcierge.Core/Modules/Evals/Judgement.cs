namespace SupportConcierge.Core.Modules.Evals;

public sealed class Judgement
{
    public double ScoreOverall { get; set; }
    public Dictionary<string, double> Subscores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Issues { get; set; } = new();
    public List<string> FixSuggestions { get; set; } = new();
    public bool PassedThreshold { get; set; }
    public string RubricId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
}

