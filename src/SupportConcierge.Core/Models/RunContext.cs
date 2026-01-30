using SupportConcierge.Core.Agents;

namespace SupportConcierge.Core.Models;

/// <summary>
/// Per-user conversation state within an issue
/// </summary>
public sealed class UserConversation
{
    public string Username { get; set; } = string.Empty;
    public int LoopCount { get; set; }
    public bool IsExhausted { get; set; }
    public DateTime FirstInteraction { get; set; }
    public DateTime LastInteraction { get; set; }
    public List<string> AskedFields { get; set; } = new();
    public CasePacket CasePacket { get; set; } = new();
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
}

/// <summary>
/// Shared findings across all users in an issue
/// </summary>
public sealed class SharedFinding
{
    public string DiscoveredBy { get; set; } = string.Empty;
    public DateTime DiscoveredAt { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Finding { get; set; } = string.Empty;
}

/// <summary>
/// Multi-user bot state - tracks separate conversations per user
/// </summary>
public sealed class BotState
{
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, UserConversation> UserConversations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SharedFinding> SharedFindings { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public bool IsActionable { get; set; }
    public int CompletenessScore { get; set; }
    public string IssueAuthor { get; set; } = string.Empty;
    public long? EngineerBriefCommentId { get; set; }
    public int BriefIterationCount { get; set; }
    
    // Backward compatibility - will be migrated to UserConversations
    [Obsolete("Use UserConversations instead")]
    public int LoopCount { get; set; }
    [Obsolete("Use UserConversations instead")]
    public List<string> AskedFields { get; set; } = new();
    [Obsolete("Use UserConversations instead")]
    public bool IsFinalized { get; set; }
    [Obsolete("Use UserConversations instead")]
    public DateTime? FinalizedAt { get; set; }
}

public sealed class RunContext
{
    public string? EventName { get; set; }
    public GitHubIssue Issue { get; set; } = new();
    public GitHubRepository Repository { get; set; } = new();
    public GitHubComment? IncomingComment { get; set; }

    public SpecPack.SpecPackConfig SpecPack { get; set; } = new();
    public BotState? State { get; set; }

    // Multi-user conversation tracking
    public string ActiveParticipant { get; set; } = string.Empty;
    public UserConversation? ActiveUserConversation { get; set; }
    public bool IsStopCommand { get; set; }
    public bool IsDiagnoseCommand { get; set; }
    public bool IsDisagreement { get; set; }
    public OffTopicAssessment? OffTopicAssessment { get; set; }
    public bool ShouldRedirectOffTopic { get; set; }

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

    // MAF Workflow specific properties
    public TriageResult? TriageResult { get; set; }
    public InvestigationResult? InvestigationResult { get; set; }
    public ResponseGenerationResult? ResponseResult { get; set; }
    public OrchestratorPlan? Plan { get; set; }
    public List<OrchestratorDecision> Decisions { get; set; } = new();
    public int? CurrentLoopCount { get; set; }

    // Execution state tracking for honest feedback and loop management
    public ExecutionState? ExecutionState { get; set; } = new();
    public UserLoopTracker? UserLoopTracker { get; set; }
}
