using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Tools;

public interface IGitHubTool
{
    Task<List<GitHubComment>> GetIssueCommentsAsync(string owner, string repo, int issueNumber, CancellationToken cancellationToken = default);
    Task<GitHubComment?> PostCommentAsync(string owner, string repo, int issueNumber, string body, CancellationToken cancellationToken = default);
    Task AddLabelsAsync(string owner, string repo, int issueNumber, List<string> labels, CancellationToken cancellationToken = default);
    Task AddAssigneesAsync(string owner, string repo, int issueNumber, List<string> assignees, CancellationToken cancellationToken = default);
    Task<string> GetFileContentAsync(string owner, string repo, string path, string? branch = null, CancellationToken cancellationToken = default);
    Task<List<GitHubIssue>> SearchIssuesAsync(string owner, string repo, string query, int maxResults = 5, CancellationToken cancellationToken = default);
}
