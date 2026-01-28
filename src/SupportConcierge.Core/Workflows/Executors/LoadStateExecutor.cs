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
/// 4. Populates RunContext with the state for decision-making
/// 
/// State persistence allows the bot to:
/// - Remember the category assigned in loop 1
/// - Track which fields have already been asked
/// - Maintain loop count to enforce max 3 iterations
/// - Prevent duplicate questions across workflow invocations
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
                input.CurrentLoopCount = loadedState.LoopCount;  // Initialize loop counter from persisted state
                
                // Validate that the issue author matches (security check)
                var issueAuthor = input.Issue?.User?.Login ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(loadedState.IssueAuthor) && 
                    loadedState.IssueAuthor != issueAuthor)
                {
                    Console.WriteLine($"[MAF] LoadState: WARNING - State author mismatch: {loadedState.IssueAuthor} vs {issueAuthor}");
                    // Still load state but log warning
                }

                // Initialize participant from issue author
                input.ActiveParticipant = issueAuthor;
            }
            else
            {
                Console.WriteLine("[MAF] LoadState: No embedded state found in comments; starting fresh");
                input.ActiveParticipant = input.Issue?.User?.Login ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] LoadState: Error loading state - {ex.Message}");
            input.ActiveParticipant = input.Issue?.User?.Login ?? string.Empty;
        }

        return input;
    }
}
