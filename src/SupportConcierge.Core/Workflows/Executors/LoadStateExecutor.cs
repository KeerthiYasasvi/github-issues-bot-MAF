using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Load persisted state from previous bot comments
/// 
/// This executor:
/// 1. Retrieves all comments on the issue
/// 2. Finds the latest bot comment with embedded state
/// 3. Extracts and validates the state
/// 4. Populates RunContext with the global state and active user conversation
/// 
/// Multi-user state persistence allows the bot to:
/// - Track separate conversations per user (each with own loop count)
/// - Remember which fields each user has been asked
/// - Maintain per-user loop count to enforce max 3 iterations per user
/// - Share findings across users while keeping conversations separate
/// </summary>
public sealed class LoadStateExecutor : Executor<RunContext, RunContext>
{
    private readonly IGitHubTool _gitHub;
    private readonly StateStoreTool _stateTool;

    public LoadStateExecutor(IGitHubTool gitHub)
        : base("load_state", ExecutorDefaults.Options, false)
    {
        _gitHub = gitHub;
        _stateTool = new StateStoreTool();
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        var owner = input.Repository?.Owner?.Login ?? string.Empty;
        var repo = input.Repository?.Name ?? string.Empty;
        var issueNumber = input.Issue?.Number ?? 0;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || issueNumber <= 0)
        {
            Console.WriteLine("[MAF] LoadState: Missing repository info; starting with no prior state");
            return input;
        }

        try
        {
            // Retrieve all comments on the issue
            var comments = await _gitHub.GetIssueCommentsAsync(owner, repo, issueNumber, ct) ?? new List<GitHubComment>();
            
            if (comments.Count == 0)
            {
                Console.WriteLine("[MAF] LoadState: No prior comments; starting fresh");
                return input;
            }

            // Get bot username from environment
            // Check both SUPPORTBOT_USERNAME (preferred) and GITHUB_ACTOR (fallback)
            var preferredBotUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";
            var actualBotUsername = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? preferredBotUsername;
            
            Console.WriteLine($"[MAF] LoadState: Looking for bot comments from: preferred={preferredBotUsername}, actual={actualBotUsername}");

            // Find the latest bot comment with state
            BotState? loadedState = null;
            foreach (var comment in comments.OrderByDescending(c => c.CreatedAt))
            {
                var commentAuthor = comment.User?.Login ?? string.Empty;
                
                // Try to extract state from this comment first
                var state = _stateTool.ExtractState(comment.Body ?? string.Empty);
                if (state == null)
                {
                    continue;  // No embedded state, skip
                }
                
                // Check if this is a bot comment (check both preferred and actual bot usernames)
                var isBotComment = commentAuthor == preferredBotUsername || commentAuthor == actualBotUsername;
                
                if (isBotComment)
                {
                    loadedState = state;
                    Console.WriteLine($"[MAF] LoadState: Found prior state from {commentAuthor} - Category={state.Category}, Loop={state.LoopCount}, Finalized={state.IsFinalized}");
                    break;
                }
                else
                {
                    Console.WriteLine($"[MAF] LoadState: Skipping state from non-bot user {commentAuthor}");
                }
            }

            if (loadedState != null)
            {
                input.State = loadedState;
                
                // Migrate legacy single-user state to multi-user if needed
                MigrateLegacyState(loadedState, input.Issue?.User?.Login ?? string.Empty);
                
                // Get or create user conversation for active participant
                var activeUser = input.ActiveParticipant;
                if (string.IsNullOrWhiteSpace(activeUser))
                {
                    activeUser = input.Issue?.User?.Login ?? string.Empty;
                    input.ActiveParticipant = activeUser;
                }

                // Check if this is a /diagnose command - create new conversation
                if (input.IsDiagnoseCommand)
                {
                    Console.WriteLine($"[MAF] LoadState: /diagnose detected - creating new conversation for {activeUser}");
                    var newConv = new UserConversation
                    {
                        Username = activeUser,
                        LoopCount = 0,
                        IsExhausted = false,
                        FirstInteraction = DateTime.UtcNow,
                        LastInteraction = DateTime.UtcNow
                    };
                    loadedState.UserConversations[activeUser] = newConv;
                    input.ActiveUserConversation = newConv;
                }
                else
                {
                    // Get or create conversation for active user
                    if (!loadedState.UserConversations.TryGetValue(activeUser, out var userConv))
                    {
                        // Issue author gets automatic conversation
                        if (activeUser == (input.Issue?.User?.Login ?? string.Empty))
                        {
                            Console.WriteLine($"[MAF] LoadState: Creating conversation for issue author {activeUser}");
                            userConv = new UserConversation
                            {
                                Username = activeUser,
                                LoopCount = 0,
                                IsExhausted = false,
                                FirstInteraction = DateTime.UtcNow,
                                LastInteraction = DateTime.UtcNow
                            };
                            loadedState.UserConversations[activeUser] = userConv;
                        }
                        else
                        {
                            Console.WriteLine($"[MAF] LoadState: No conversation for {activeUser} and not issue author");
                            // GuardrailsExecutor will handle this
                        }
                    }
                    else
                    {
                        userConv.LastInteraction = DateTime.UtcNow;
                        Console.WriteLine($"[MAF] LoadState: Loaded conversation for {activeUser} - Loop={userConv.LoopCount}, Finalized={userConv.IsFinalized}");
                    }

                    input.ActiveUserConversation = userConv;
                }

                input.CurrentLoopCount = input.ActiveUserConversation?.LoopCount ?? 0;
                
                // Validate that the issue author matches (security check)
                var issueAuthor = input.Issue?.User?.Login ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(loadedState.IssueAuthor) && 
                    loadedState.IssueAuthor != issueAuthor)
                {
                    Console.WriteLine($"[MAF] LoadState: WARNING - State author mismatch: {loadedState.IssueAuthor} vs {issueAuthor}");
                    // Still load state but log warning
                }
            }
            else
            {
                Console.WriteLine("[MAF] LoadState: No embedded state found in comments; starting fresh");
                
                // Initialize new state with issue author conversation
                var issueAuthor = input.Issue?.User?.Login ?? string.Empty;
                input.ActiveParticipant = issueAuthor;
                
                var newState = new BotState
                {
                    IssueAuthor = issueAuthor,
                    LastUpdated = DateTime.UtcNow
                };

                var authorConv = new UserConversation
                {
                    Username = issueAuthor,
                    LoopCount = 0,
                    IsExhausted = false,
                    FirstInteraction = DateTime.UtcNow,
                    LastInteraction = DateTime.UtcNow
                };
                
                newState.UserConversations[issueAuthor] = authorConv;
                input.State = newState;
                input.ActiveUserConversation = authorConv;
                input.CurrentLoopCount = 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] LoadState: Error loading state - {ex.Message}");
            input.ActiveParticipant = input.Issue?.User?.Login ?? string.Empty;
        }

        return input;
    }

    /// <summary>
    /// Migrate legacy single-user state to multi-user format
    /// </summary>
    private void MigrateLegacyState(BotState state, string issueAuthor)
    {
        // Check if state has legacy fields populated but no UserConversations
#pragma warning disable CS0618 // Type or member is obsolete
        if (state.UserConversations.Count == 0 && state.LoopCount > 0)
        {
            Console.WriteLine("[MAF] LoadState: Migrating legacy single-user state to multi-user format");
            
            var legacyConv = new UserConversation
            {
                Username = issueAuthor,
                LoopCount = state.LoopCount,
                IsExhausted = state.LoopCount >= 3,
                FirstInteraction = state.LastUpdated,
                LastInteraction = state.LastUpdated,
                AskedFields = state.AskedFields.ToList(),
                IsFinalized = state.IsFinalized,
                FinalizedAt = state.FinalizedAt
            };
            
            state.UserConversations[issueAuthor] = legacyConv;
            Console.WriteLine($"[MAF] LoadState: Migrated legacy state - {issueAuthor} Loop={legacyConv.LoopCount}");
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
