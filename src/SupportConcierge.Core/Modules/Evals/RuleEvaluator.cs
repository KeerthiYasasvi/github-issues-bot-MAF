using System.Text.RegularExpressions;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Core.Modules.Evals;

public sealed class RuleEvaluator
{
    private readonly SchemaValidator _schemaValidator;

    public RuleEvaluator(SchemaValidator schemaValidator)
    {
        _schemaValidator = schemaValidator;
    }

    public (Dictionary<string, double> Subscores, List<string> Issues, List<string> Suggestions) Evaluate(
        RubricDefinition rubric,
        EvalContext context)
    {
        var subscores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        var suggestions = new List<string>();

        foreach (var item in rubric.Items.Where(i => i.Type.Equals("rule", StringComparison.OrdinalIgnoreCase)))
        {
            var (score, itemIssues, itemSuggestions) = EvaluateRule(item, context);
            subscores[item.Id] = Math.Max(0, Math.Min(item.MaxPoints, score));
            issues.AddRange(itemIssues);
            suggestions.AddRange(itemSuggestions);
        }

        return (subscores, issues, suggestions);
    }

    private (double Score, List<string> Issues, List<string> Suggestions) EvaluateRule(RubricItem item, EvalContext context)
    {
        var issues = new List<string>();
        var suggestions = new List<string>();
        var max = item.MaxPoints;
        var score = max;

        switch (item.RuleId?.ToLowerInvariant())
        {
            case "tool_allowlist":
                if (context.ToolAllowList.Length > 0 && context.ToolsUsed.Length > 0)
                {
                    var allowed = new HashSet<string>(context.ToolAllowList, StringComparer.OrdinalIgnoreCase);
                    var invalid = context.ToolsUsed.Where(t => !allowed.Contains(t)).ToList();
                    if (invalid.Count > 0)
                    {
                        score = 0;
                        issues.Add($"Tool allow-list violated: {string.Join(", ", invalid)}");
                        suggestions.Add("Use only allow-listed tools for this phase.");
                    }
                }
                break;
            case "schema_valid":
                if (!string.IsNullOrWhiteSpace(context.ExpectedJsonSchema) && !string.IsNullOrWhiteSpace(context.OutputText))
                {
                    if (!_schemaValidator.TryValidate(context.OutputText, context.ExpectedJsonSchema, out var errors))
                    {
                        score = 0;
                        issues.Add($"Schema validation failed: {string.Join(" | ", errors.Take(3))}");
                        suggestions.Add("Return valid JSON that matches the schema.");
                    }
                }
                break;
            case "secret_request_ban":
                if (ContainsSecretRequest(context.OutputText))
                {
                    score = 0;
                    issues.Add("Output requests secrets or tokens.");
                    suggestions.Add("Do not request passwords, tokens, or secrets.");
                }
                break;
            case "question_mapping":
                var (qIssues, qSuggestions) = EvaluateQuestionMapping(context);
                if (qIssues.Count > 0)
                {
                    score = 0;
                    issues.AddRange(qIssues);
                    suggestions.AddRange(qSuggestions);
                }
                break;
            case "no_hallucinated_versions":
                if (HasUnseenVersionInfo(context.InputText, context.OutputText))
                {
                    score = Math.Max(0, max - 1);
                    issues.Add("Output includes version/env details not present in input.");
                    suggestions.Add("Avoid asserting specific versions unless provided by the user.");
                }
                break;
        }

        return (score, issues, suggestions);
    }

    private static bool ContainsSecretRequest(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var lowered = output.ToLowerInvariant();
        return lowered.Contains("api key") || lowered.Contains("token") || lowered.Contains("password") || lowered.Contains("secret");
    }

    private static (List<string> Issues, List<string> Suggestions) EvaluateQuestionMapping(EvalContext context)
    {
        var issues = new List<string>();
        var suggestions = new List<string>();

        var questions = context.FollowUpQuestions.Select(q => q.Question).Where(q => !string.IsNullOrWhiteSpace(q)).ToList();
        if (questions.Count == 0)
        {
            return (issues, suggestions);
        }

        if (questions.Count > 3)
        {
            issues.Add("More than 3 follow-up questions were asked.");
            suggestions.Add("Ask at most 3 follow-up questions.");
        }

        var distinct = questions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count != questions.Count)
        {
            issues.Add("Duplicate follow-up questions detected.");
            suggestions.Add("Remove duplicate questions.");
        }

        if (context.MissingFields.Count > 0)
        {
            foreach (var q in questions)
            {
                var matched = context.MissingFields.Any(m => q.Contains(m, StringComparison.OrdinalIgnoreCase));
                if (!matched)
                {
                    issues.Add("Follow-up question does not map to a missing field.");
                    suggestions.Add("Ensure each question targets a specific missing field.");
                    break;
                }
            }
        }

        return (issues, suggestions);
    }

    private static bool HasUnseenVersionInfo(string input, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var versionPattern = new Regex(@"\b\d+\.\d+(\.\d+)?\b");
        var outputVersions = versionPattern.Matches(output).Select(m => m.Value).Distinct().ToList();
        if (outputVersions.Count == 0)
        {
            return false;
        }

        var inputText = input ?? string.Empty;
        return outputVersions.Any(v => !inputText.Contains(v, StringComparison.OrdinalIgnoreCase));
    }
}

