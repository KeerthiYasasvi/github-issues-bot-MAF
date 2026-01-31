namespace SupportConcierge.Core.Modules.Models;

public sealed class EventInput
{
    public string? EventName { get; set; }
    public string? Action { get; set; }
    public GitHubIssue Issue { get; set; } = new();
    public GitHubRepository Repository { get; set; } = new();
    public GitHubComment? Comment { get; set; }
}

