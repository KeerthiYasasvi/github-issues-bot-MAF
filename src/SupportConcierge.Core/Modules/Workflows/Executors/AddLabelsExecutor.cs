using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Core.Modules.Workflows.Executors;

/// <summary>
/// MAF Executor: Add issue labels based on triage category.
/// </summary>
public sealed class AddLabelsExecutor : Executor<RunContext, RunContext>
{
    private readonly IGitHubTool _gitHub;

    public AddLabelsExecutor(IGitHubTool gitHub)
        : base("add_labels", ExecutorDefaults.Options, false)
    {
        _gitHub = gitHub;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        if (input.ShouldStop || input.ShouldRedirectOffTopic)
        {
            return input;
        }

        var owner = input.Repository?.Owner?.Login ?? string.Empty;
        var repo = input.Repository?.Name ?? string.Empty;
        var issueNumber = input.Issue?.Number ?? 0;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || issueNumber <= 0)
        {
            return input;
        }

        var category = input.CategoryDecision?.Category ?? string.Empty;
        var label = MapCategoryToLabel(category);
        if (string.IsNullOrWhiteSpace(label))
        {
            return input;
        }

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (input.Issue?.Labels != null)
        {
            foreach (var lbl in input.Issue.Labels)
            {
                if (!string.IsNullOrWhiteSpace(lbl.Name))
                {
                    existing.Add(lbl.Name);
                }
            }
        }

        if (existing.Contains(label))
        {
            Console.WriteLine($"[MAF] Labels: '{label}' already present; skipping.");
            return input;
        }

        Console.WriteLine($"[MAF] Labels: Adding '{label}' based on category '{category}'.");
        await _gitHub.AddLabelsAsync(owner, repo, issueNumber, new List<string> { label }, ct);
        return input;
    }

    private static string MapCategoryToLabel(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "needs-triage";
        }

        var cat = category.Trim().ToLowerInvariant();
        if (cat.Contains("documentation"))
        {
            return "documentation";
        }
        if (cat.Contains("feature"))
        {
            return "feature";
        }
        if (cat.Contains("configuration") || cat.Contains("config"))
        {
            return "configuration";
        }
        if (cat.Contains("build"))
        {
            return "build";
        }
        if (cat.Contains("dependency"))
        {
            return "dependencies";
        }
        if (cat.Contains("environment"))
        {
            return "environment";
        }
        if (cat.Contains("bug") || cat.Contains("runtime_error") || cat.Contains("error"))
        {
            return "bug";
        }

        return "question";
    }
}

