namespace SupportConcierge.Models;

public sealed class BotState
{
    public string Category { get; set; } = string.Empty;
    public int LoopCount { get; set; }
    public List<string> AskedFields { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public bool IsActionable { get; set; }
    public int CompletenessScore { get; set; }
    public string IssueAuthor { get; set; } = string.Empty;
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public long? EngineerBriefCommentId { get; set; }
    public int BriefIterationCount { get; set; }

    // Judge state fields for cross-run persistence
    public int FollowUpJudgeAttempts { get; set; } // Track judge scoring attempts (0, 1, 2)
    public int BriefJudgeAttempts { get; set; } // Track judge scoring attempts (0, 1, 2)
    public int FollowUpJudgeScore { get; set; } // Last score from judge (1-5)
    public int BriefJudgeScore { get; set; } // Last score from judge (1-5)
    public List<string> FollowUpJudgeRationales { get; set; } = new(); // Track judge feedback across attempts
    public List<string> BriefJudgeRationales { get; set; } = new(); // Track judge feedback across attempts
}

public sealed class RunContext
{
    public string? EventName { get; set; }
    public GitHubIssue Issue { get; set; } = new();
    public GitHubRepository Repository { get; set; } = new();
    public GitHubComment? IncomingComment { get; set; }

    public SpecPack.SpecPackConfig SpecPack { get; set; } = new();
    public BotState? State { get; set; }

    public string ActiveParticipant { get; set; } = string.Empty;
    public bool IsStopCommand { get; set; }
    public bool IsDiagnoseCommand { get; set; }
    public bool IsDisagreement { get; set; }

    public CategoryDecision? CategoryDecision { get; set; }
    public CasePacket CasePacket { get; } = new();
    public ScoringResult? Scoring { get; set; }
    public List<FollowUpQuestion> FollowUpQuestions { get; set; } = new();
    public EngineerBrief? Brief { get; set; }
    public List<DuplicateReference> Duplicates { get; set; } = new();
    public string RepoDocs { get; set; } = string.Empty;
    public string Playbook { get; set; } = string.Empty;

    public bool ShouldStop { get; set; }
    public bool ShouldAskFollowUps { get; set; }
    public bool ShouldFinalize { get; set; }
    public bool ShouldEscalate { get; set; }
    public string? StopReason { get; set; }

    public bool DryRun { get; set; }
    public bool WriteMode { get; set; }

    public Dictionary<string, string> DecisionPath { get; } = new(StringComparer.OrdinalIgnoreCase);
}
