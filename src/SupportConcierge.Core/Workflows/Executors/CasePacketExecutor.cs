using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Guardrails;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.SpecPack;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Workflows.Executors;

public sealed class CasePacketExecutor : Executor<RunContext, RunContext>
{
    private const int MaxComments = 5;
    private readonly IGitHubTool _gitHub;
    private readonly CasePacketAgent _casePacketAgent;

    public CasePacketExecutor(IGitHubTool gitHub, CasePacketAgent casePacketAgent)
        : base("case_packet", ExecutorDefaults.Options, false)
    {
        _gitHub = gitHub;
        _casePacketAgent = casePacketAgent;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        Console.WriteLine("[MAF] CasePacket: Extracting structured fields");

        var owner = input.Repository?.Owner?.Login ?? string.Empty;
        var repo = input.Repository?.Name ?? string.Empty;
        var issueNumber = input.Issue?.Number ?? 0;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || issueNumber <= 0)
        {
            Console.WriteLine("[MAF] CasePacket: Missing repository context");
            return input;
        }

        var category = ResolveCategory(input);
        var checklist = ResolveChecklist(input.SpecPack, category);
        var requiredFields = ResolveRequiredFields(input, checklist);

        if (requiredFields.Count == 0)
        {
            Console.WriteLine("[MAF] CasePacket: No required fields for category");
            return input;
        }

        var comments = await SafeGetComments(owner, repo, issueNumber, ct);
        var commentText = BuildCommentsText(comments);

        var deterministicFields = ExtractDeterministicFields(input.Issue?.Body ?? string.Empty, commentText);
        var casePacket = new CasePacket();

        foreach (var field in requiredFields)
        {
            var normalizedField = NormalizeFieldName(field);
            if (deterministicFields.TryGetValue(normalizedField, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                casePacket.Fields[normalizedField] = value;
                casePacket.DeterministicFields.Add(normalizedField);
            }
        }

        var missingFields = requiredFields
            .Select(NormalizeFieldName)
            .Where(field => !casePacket.Fields.ContainsKey(field) || string.IsNullOrWhiteSpace(casePacket.Fields[field]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingFields.Count > 0)
        {
            var llmExtracted = await _casePacketAgent.ExtractAsync(
                input.Issue?.Body ?? string.Empty,
                commentText,
                missingFields,
                ct);

            foreach (var kvp in llmExtracted)
            {
                if (!casePacket.Fields.ContainsKey(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    casePacket.Fields[kvp.Key] = kvp.Value;
                }
            }
        }

        input.CasePacket.Fields.Clear();
        foreach (var kvp in casePacket.Fields)
        {
            input.CasePacket.Fields[kvp.Key] = kvp.Value;
        }

        input.CasePacket.DeterministicFields.Clear();
        foreach (var field in casePacket.DeterministicFields)
        {
            input.CasePacket.DeterministicFields.Add(field);
        }

        if (checklist != null)
        {
            var validators = new Validators(input.SpecPack.Validators);
            var scorer = new CompletenessScorer(validators);
            input.Scoring = scorer.Score(input.CasePacket.Fields, checklist);
            if (input.State != null)
            {
                input.State.Category = checklist.Category;
                input.State.CompletenessScore = input.Scoring.Score;
                input.State.IsActionable = input.Scoring.IsActionable;
            }
        }

        if (input.ActiveUserConversation != null)
        {
            input.ActiveUserConversation.CasePacket = input.CasePacket;
        }

        return input;
    }

    private static string ResolveCategory(RunContext input)
    {
        var category = input.CategoryDecision?.Category
            ?? input.TriageResult?.Categories.FirstOrDefault()
            ?? string.Empty;

        category = category.ToLowerInvariant();

        return category switch
        {
            _ when category.Contains("build") => "build",
            _ when category.Contains("runtime") => "runtime",
            _ when category.Contains("environment") => "setup",
            _ when category.Contains("setup") => "setup",
            _ when category.Contains("documentation") => "docs",
            _ when category.Contains("doc") => "docs",
            _ when category.Contains("bug") => "bug",
            _ => category
        };
    }

    private static CategoryChecklist? ResolveChecklist(SpecPackConfig specPack, string category)
    {
        if (specPack.Checklists.TryGetValue(category, out var checklist))
        {
            return checklist;
        }

        return null;
    }

    private static List<string> ResolveRequiredFields(RunContext input, CategoryChecklist? checklist)
    {
        if (input.TriageResult?.UsesCustomCategory == true && input.TriageResult.CustomCategory?.RequiredFields?.Count > 0)
        {
            return input.TriageResult.CustomCategory.RequiredFields;
        }

        if (checklist != null)
        {
            return checklist.RequiredFields.Select(r => r.Name).ToList();
        }

        return GetDefaultFieldsFromSchema();
    }

    private static Dictionary<string, string> ExtractDeterministicFields(string issueBody, string commentText)
    {
        var parser = new IssueFormParser();
        var fromIssue = parser.ParseIssueForm(issueBody);
        var fromIssuePairs = parser.ExtractKeyValuePairs(issueBody);
        var fromComments = parser.ExtractKeyValuePairs(commentText);

        var merged = parser.MergeFields(fromIssue, fromIssuePairs, fromComments);
        return merged.ToDictionary(kvp => NormalizeFieldName(kvp.Key), kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeFieldName(string fieldName)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(fieldName, "[^\\w\\s]", string.Empty);
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "\\s+", "_");
        return normalized.ToLowerInvariant();
    }

    private static List<string> GetDefaultFieldsFromSchema()
    {
        try
        {
            var json = JsonDocument.Parse(SupportConcierge.Core.Agents.Schemas.CasePacketSchema);
            var required = json.RootElement.GetProperty("required");
            return required.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<List<GitHubComment>> SafeGetComments(string owner, string repo, int issueNumber, CancellationToken ct)
    {
        try
        {
            return await _gitHub.GetIssueCommentsAsync(owner, repo, issueNumber, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] CasePacket: Failed to fetch comments - {ex.Message}");
            return new List<GitHubComment>();
        }
    }

    private static string BuildCommentsText(List<GitHubComment> comments)
    {
        var botUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";
        var recent = comments
            .Where(c => !string.Equals(c.User?.Login ?? string.Empty, botUsername, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.CreatedAt)
            .Take(MaxComments)
            .OrderBy(c => c.CreatedAt)
            .ToList();

        var sb = new StringBuilder();
        foreach (var comment in recent)
        {
            if (string.IsNullOrWhiteSpace(comment.Body))
            {
                continue;
            }

            sb.AppendLine($"[{comment.User?.Login ?? "user"}] {comment.Body}");
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }
}
