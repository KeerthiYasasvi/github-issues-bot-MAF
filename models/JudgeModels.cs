namespace SupportConcierge.Models;

public sealed class JudgeResult
{
    public int Score { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
    public List<FollowUpQuestion> RevisedQuestions { get; set; } = new();
    public EngineerBrief? RevisedBrief { get; set; }
}
