using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SupportConcierge.Models;

namespace SupportConcierge.Tools;

public sealed class GitHubTool : IGitHubTool
{
    private readonly HttpClient _httpClient;
    private readonly bool _dryRun;
    private readonly bool _writeMode;

    public GitHubTool(string token, bool dryRun, bool writeMode, HttpClient? httpClient = null)
    {
        _dryRun = dryRun;
        _writeMode = writeMode;
        _httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SupportConcierge", "1.0"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<List<GitHubComment>> GetIssueCommentsAsync(string owner, string repo, int issueNumber, CancellationToken cancellationToken = default)
    {
        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/comments";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<List<GitHubComment>>(json) ?? new List<GitHubComment>();
    }

    public async Task<GitHubComment?> PostCommentAsync(string owner, string repo, int issueNumber, string body, CancellationToken cancellationToken = default)
    {
        if (_dryRun || !_writeMode)
        {
            return null;
        }

        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/comments";
        var request = new CreateCommentRequest { Body = body };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<GitHubComment>(json);
    }

    public async Task AddLabelsAsync(string owner, string repo, int issueNumber, List<string> labels, CancellationToken cancellationToken = default)
    {
        if (labels.Count == 0 || _dryRun || !_writeMode)
        {
            return;
        }

        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/labels";
        var request = new AddLabelsRequest { Labels = labels };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddAssigneesAsync(string owner, string repo, int issueNumber, List<string> assignees, CancellationToken cancellationToken = default)
    {
        if (assignees.Count == 0 || _dryRun || !_writeMode)
        {
            return;
        }

        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/assignees";
        var request = new AddAssigneesRequest { Assignees = assignees };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> GetFileContentAsync(string owner, string repo, string path, string? branch = null, CancellationToken cancellationToken = default)
    {
        var url = $"repos/{owner}/{repo}/contents/{path}";
        if (!string.IsNullOrEmpty(branch))
        {
            url += $"?ref={branch}";
        }

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        if (element.TryGetProperty("content", out var contentElement))
        {
            var base64 = contentElement.GetString() ?? string.Empty;
            base64 = base64.Replace("\n", string.Empty).Replace("\r", string.Empty);
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }

        return string.Empty;
    }

    public async Task<List<GitHubIssue>> SearchIssuesAsync(string owner, string repo, string query, int maxResults = 5, CancellationToken cancellationToken = default)
    {
        var searchQuery = $"repo:{owner}/{repo} is:issue {query}";
        var encoded = Uri.EscapeDataString(searchQuery);
        var url = $"search/issues?q={encoded}&per_page={maxResults}&sort=created&order=desc";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new List<GitHubIssue>();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        if (element.TryGetProperty("items", out var itemsElement))
        {
            return JsonSerializer.Deserialize<List<GitHubIssue>>(itemsElement.GetRawText()) ?? new List<GitHubIssue>();
        }

        return new List<GitHubIssue>();
    }
}
