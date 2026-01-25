using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Tools;

public sealed class FakeGitHubTool : IGitHubTool
{
    private readonly List<GitHubComment> _comments;
    private readonly Dictionary<string, string> _files;
    private readonly List<string> _labels = new();
    private readonly List<string> _assignees = new();

    public FakeGitHubTool(List<GitHubComment> comments, Dictionary<string, string>? files = null)
    {
        _comments = comments;
        _files = files ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public Task<List<GitHubComment>> GetIssueCommentsAsync(string owner, string repo, int issueNumber, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_comments.ToList());
    }

    public Task<GitHubComment?> PostCommentAsync(string owner, string repo, int issueNumber, string body, CancellationToken cancellationToken = default)
    {
        var comment = new GitHubComment
        {
            Id = DateTime.UtcNow.Ticks,
            Body = body,
            User = new GitHubUser { Login = "github-actions[bot]" },
            CreatedAt = DateTime.UtcNow
        };
        _comments.Add(comment);
        return Task.FromResult<GitHubComment?>(comment);
    }

    public Task AddLabelsAsync(string owner, string repo, int issueNumber, List<string> labels, CancellationToken cancellationToken = default)
    {
        _labels.AddRange(labels);
        return Task.CompletedTask;
    }

    public Task AddAssigneesAsync(string owner, string repo, int issueNumber, List<string> assignees, CancellationToken cancellationToken = default)
    {
        _assignees.AddRange(assignees);
        return Task.CompletedTask;
    }

    public Task<string> GetFileContentAsync(string owner, string repo, string path, string? branch = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_files.TryGetValue(path, out var content) ? content : string.Empty);
    }

    public Task<List<GitHubIssue>> SearchIssuesAsync(string owner, string repo, string query, int maxResults = 5, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<GitHubIssue>());
    }
}
