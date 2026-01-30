using SupportConcierge.Core.SpecPack;

namespace SupportConcierge.Core.Models;

public sealed class EvalScenario
{
    public string Name { get; set; } = string.Empty;
    public string EventName { get; set; } = "issues";
    public GitHubIssue Issue { get; set; } = new();
    public GitHubRepository Repository { get; set; } = new();
    public List<GitHubComment> Comments { get; set; } = new();
    public List<EvalEvent> Events { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? SpecPackPath { get; set; }
    public SpecPackConfig SpecPack { get; set; } = new();
    public EvalExpectations? Expectations { get; set; }
}

public sealed class EvalEvent
{
    public string EventName { get; set; } = "issue_comment";
    public string CommentBody { get; set; } = string.Empty;
    public string CommentAuthor { get; set; } = string.Empty;
}

public sealed class EvalExpectations
{
    public string? ExpectedDecision { get; set; }
    public string? ExpectedCategory { get; set; }
    public int? MinCompleteness { get; set; }
    public int? MaxCompleteness { get; set; }
    public int? MinFollowUpQuestions { get; set; }
    public int? MaxFollowUpQuestions { get; set; }
    public List<string> RequiredQuestionKeywords { get; set; } = new();
    public List<string> ForbiddenQuestionKeywords { get; set; } = new();
    public List<string> ExpectedTools { get; set; } = new();
    public bool? ExpectOffTopicRedirect { get; set; }
    public bool? ExpectNoSecretsRequested { get; set; }
    public bool? ExpectTriageRefinement { get; set; }
    public bool? ExpectResearchDeepDive { get; set; }
    public bool? ExpectResponseRefinement { get; set; }
    public bool? ExpectInfoSufficient { get; set; }
    public Dictionary<string, int> ExpectedUserLoopCounts { get; set; } = new();
    public List<string> ExpectedAllowedUsers { get; set; } = new();
    public double? MaxLatencyMs { get; set; }
    public int? MaxTotalTokens { get; set; }
}

public sealed class EvalResult
{
    public string ScenarioName { get; set; } = string.Empty;
    public string DecisionPath { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int CompletenessScore { get; set; }
    public List<string> MissingFields { get; set; } = new();
    public List<FollowUpQuestion> FollowUps { get; set; } = new();
    public EngineerBrief? Brief { get; set; }
    public bool SchemaValid { get; set; }
    public List<string> SchemaErrors { get; set; } = new();
    public List<string> HallucinationWarnings { get; set; } = new();
    public TokenUsageSummary TokenUsage { get; set; } = new();
    public double TotalLatencyMs { get; set; }
    public bool Passed { get; set; }
    public List<string> FailureReasons { get; set; } = new();
}

public sealed class FollowUpEvalScenario
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IssueBody { get; set; } = string.Empty;
    public List<string> MissingFields { get; set; } = new();
    public List<string> AskedBefore { get; set; } = new();
}

public sealed class BriefEvalScenario
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IssueBody { get; set; } = string.Empty;
    public Dictionary<string, string> ExtractedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Playbook { get; set; } = string.Empty;
    public string RepoDocs { get; set; } = string.Empty;
}

public sealed class FollowUpEvalResult
{
    public string ScenarioName { get; set; } = string.Empty;
    public List<FollowUpQuestion> Questions { get; set; } = new();
    public int Score { get; set; }
    public bool Failed { get; set; }
    public List<string> Notes { get; set; } = new();
    public JudgeResult? JudgeResult { get; set; }
}

public sealed class BriefEvalResult
{
    public string ScenarioName { get; set; } = string.Empty;
    public EngineerBrief? Brief { get; set; }
    public int Score { get; set; }
    public bool Failed { get; set; }
    public List<string> Notes { get; set; } = new();
    public JudgeResult? JudgeResult { get; set; }
}
