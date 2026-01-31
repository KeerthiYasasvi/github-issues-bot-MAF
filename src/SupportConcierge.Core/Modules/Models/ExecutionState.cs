using System.Text.Json.Serialization;

namespace SupportConcierge.Core.Modules.Models;

/// <summary>
/// Tracks the execution state of a single bot loop/interaction.
/// Used for honest escalation messages and state-aware decisions.
/// </summary>
public class ExecutionState
{
    [JsonPropertyName("loop_number")]
    public int LoopNumber { get; set; }

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("issue_number")]
    public int IssueNumber { get; set; }

    [JsonPropertyName("total_user_loops")]
    public int TotalUserLoops { get; set; } = 3;

    [JsonPropertyName("questions_asked")]
    public List<string> QuestionsAsked { get; set; } = new();

    [JsonPropertyName("information_gathered")]
    public List<string> InformationGathered { get; set; } = new();

    [JsonPropertyName("missing_information")]
    public List<string> MissingInformation { get; set; } = new();

    [JsonPropertyName("questions_answered_by_user")]
    public bool QuestionsAnswered { get; set; }

    [JsonPropertyName("triage_score")]
    public decimal? TriageScore { get; set; }

    [JsonPropertyName("research_score")]
    public decimal? ResearchScore { get; set; }

    [JsonPropertyName("response_score")]
    public decimal? ResponseScore { get; set; }

    [JsonPropertyName("triage_critique_issues")]
    public List<string> TriageCritiqueIssues { get; set; } = new();

    [JsonPropertyName("research_critique_issues")]
    public List<string> ResearchCritiqueIssues { get; set; } = new();

    [JsonPropertyName("response_critique_issues")]
    public List<string> ResponseCritiqueIssues { get; set; } = new();

    [JsonPropertyName("loop_action_taken")]
    public string? LoopActionTaken { get; set; } // "ask_questions", "provide_response", "escalate"

    [JsonPropertyName("is_user_exhausted")]
    public bool IsUserExhausted { get; set; }

    [JsonPropertyName("last_update")]
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Helper method to generate honest escalation context based on actual execution
    /// </summary>
    public string GenerateExecutionSummary()
    {
        var summary = $"Loop {this.LoopNumber}/{this.TotalUserLoops} | ";

        if (QuestionsAsked.Count > 0)
        {
            summary += $"Asked {QuestionsAsked.Count} questions | ";
        }

        if (InformationGathered.Count > 0)
        {
            summary += $"Gathered {InformationGathered.Count} data points | ";
        }

        if (MissingInformation.Count > 0)
        {
            summary += $"Missing {MissingInformation.Count} pieces | ";
        }

        summary += $"Action: {this.LoopActionTaken}";

        return summary;
    }

    /// <summary>
    /// Generates honest context about what actually happened for the comment template
    /// </summary>
    public Dictionary<string, object> GetCommentTemplateContext()
    {
        return new Dictionary<string, object>
        {
            ["LOOP_NUMBER"] = this.LoopNumber,
            ["TOTAL_USER_LOOPS"] = this.TotalUserLoops,
            ["QUESTIONS_ASKED_COUNT"] = QuestionsAsked.Count,
            ["QUESTIONS_ASKED_LIST"] = string.Join("\n", QuestionsAsked.Select((q, i) => $"{i + 1}. {q}")),
            ["INFORMATION_GATHERED_COUNT"] = InformationGathered.Count,
            ["INFORMATION_GATHERED_LIST"] = string.Join("\n", InformationGathered.Select((i, idx) => $"- {i}")),
            ["MISSING_INFORMATION_COUNT"] = MissingInformation.Count,
            ["MISSING_INFORMATION_LIST"] = string.Join("\n", MissingInformation.Select((m, i) => $"- {m}")),
            ["TRIAGE_SCORE"] = TriageScore?.ToString("0.##") ?? "N/A",
            ["RESEARCH_SCORE"] = ResearchScore?.ToString("0.##") ?? "N/A",
            ["RESPONSE_SCORE"] = ResponseScore?.ToString("0.##") ?? "N/A",
            ["ACTION_TAKEN"] = this.LoopActionTaken ?? "unknown",
            ["IS_FINAL_LOOP"] = this.LoopNumber >= this.TotalUserLoops,
            ["IS_ESCALATION"] = this.IsUserExhausted
        };
    }
}

/// <summary>
/// Tracks user exhaustion per issue to enforce loop limits
/// </summary>
public class UserLoopTracker
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("issue_number")]
    public int IssueNumber { get; set; }

    [JsonPropertyName("loop_count")]
    public int LoopCount { get; set; }

    [JsonPropertyName("is_exhausted")]
    public bool IsExhausted { get; set; }

    [JsonPropertyName("last_interaction")]
    public DateTime LastInteraction { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("escalation_message_posted")]
    public bool EscalationMessagePosted { get; set; }

    /// <summary>
    /// Check if user has exhausted their loops (max 3 per issue)
    /// </summary>
    public bool HasExhaustedLoops(int maxLoops = 3)
    {
        return LoopCount >= maxLoops;
    }

    /// <summary>
    /// Increment loop count and check if exhausted
    /// </summary>
    public void IncrementLoop(int maxLoops = 3)
    {
        LoopCount++;
        IsExhausted = LoopCount >= maxLoops;
        LastInteraction = DateTime.UtcNow;
    }
}

