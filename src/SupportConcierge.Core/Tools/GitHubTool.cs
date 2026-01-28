using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Tools;

public sealed class GitHubTool : IGitHubTool
{
    private readonly HttpClient _httpClient;
    private readonly bool _dryRun;
    private readonly bool _writeMode;

    public GitHubTool(string token, bool dryRun, bool writeMode, HttpClient? httpClient = null)
    {
        _dryRun = dryRun;
        _writeMode = writeMode;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SupportConcierge", "1.0"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<List<GitHubComment>> GetIssueCommentsAsync(string owner, string repo, int issueNumber, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}/comments";
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

        Console.WriteLine($"[GitHubTool] Attempting to post comment to {owner}/{repo}#{issueNumber} using REST API");

        // Use REST API instead of GraphQL - REST API properly respects GITHUB_TOKEN identity
        // when workflow has 'contents: write' permission
        var url = $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}/comments";
        
        var request = new
        {
            body = body
        };

        Console.WriteLine($"[GitHubTool] Posting to REST API: {url}");
        
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            
            Console.WriteLine($"[GitHubTool] REST API response status: {response.StatusCode}");
            
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"[GitHubTool] REST API response body length: {json.Length} chars");
            
            response.EnsureSuccessStatusCode();

            // Parse the response to get the comment details
            var comment = JsonSerializer.Deserialize<GitHubComment>(json);
            
            if (comment != null)
            {
                Console.WriteLine($"[GitHubTool] Comment posted successfully via REST API");
                Console.WriteLine($"[GitHubTool] Comment ID: {comment.Id}, Author: {comment.User?.Login ?? "unknown"}");
                return comment;
            }

            Console.WriteLine($"[GitHubTool] Could not parse REST API response");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitHubTool] Exception during REST API posting: {ex.Message}");
            Console.WriteLine($"[GitHubTool] Exception stack trace: {ex.StackTrace}");
            throw;
        }
    }

    public async Task AddLabelsAsync(string owner, string repo, int issueNumber, List<string> labels, CancellationToken cancellationToken = default)
    {
        if (labels.Count == 0 || _dryRun || !_writeMode)
        {
            return;
        }

        var url = $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}/labels";
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

        var url = $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}/assignees";
        var request = new AddAssigneesRequest { Assignees = assignees };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> GetFileContentAsync(string owner, string repo, string path, string? branch = null, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
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
        var url = $"https://api.github.com/search/issues?q={encoded}&per_page={maxResults}&sort=created&order=desc";

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
