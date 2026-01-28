using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Prompts;
using SupportConcierge.Core.Schemas;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Agents;

/// <summary>
/// Enhanced TriageAgent with hybrid category classification.
/// If confidence >= 0.75: Use predefined categories
/// If confidence < 0.75: Activate custom category with LLM-suggested checklist
/// 
/// Responsibilities:
/// - Classify issue into categories
/// - Extract key details
/// - Fallback to custom categories when uncertain
/// - Respond to critique feedback with refinements
/// </summary>
public class EnhancedTriageAgent
{
    private readonly ILlmClient _llmClient;
    private readonly SchemaValidator _schemaValidator;
    private const decimal ConfidenceThreshold = 0.75m;

    private static readonly List<string> PredefinedCategories = new()
    {
        "build_issue",
        "runtime_error",
        "dependency_conflict",
        "documentation_issue",
        "feature_request",
        "bug_report",
        "configuration_error",
        "environment_setup"
    };

    public EnhancedTriageAgent(ILlmClient llmClient, SchemaValidator schemaValidator)
    {
        _llmClient = llmClient;
        _schemaValidator = schemaValidator;
    }

    /// <summary>
    /// Initial classification and extraction
    /// </summary>
    public async Task<TriageResult> ClassifyAndExtractAsync(RunContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var schema = OrchestrationSchemas.GetTriageRefinementSchema();
            var categoriesJson = string.Join(", ", PredefinedCategories.Select(c => $"\"{c}\""));

            var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
                "triage-classify.md",
                new Dictionary<string, string>
                {
                    ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                    ["ISSUE_BODY"] = context.Issue.Body ?? string.Empty,
                    ["CATEGORIES_JSON"] = categoriesJson
                },
                cancellationToken);

            var request = new LlmRequest
            {
                Messages = new List<LlmMessage>
                {
                    new() { Role = "system", Content = systemPrompt },
                    new() { Role = "user", Content = userPrompt }
                },
                JsonSchema = schema,
                SchemaName = "TriageRefinement",
                Temperature = 0.3
            };

            var response = await _llmClient.CompleteAsync(request, cancellationToken);
            return ParseTriageResponse(response, context);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] Triage: Error during classification: {ex.GetType().Name}: {ex.Message}");
            return new TriageResult
            {
                Categories = new List<string> { "unclassified" },
                ExtractedDetails = new Dictionary<string, string> { { "title", context.Issue.Title } },
                ConfidenceScore = 0.0m,
                Reasoning = $"Triage failed: {ex.Message}",
                CustomCategory = null
            };
        }
    }

    /// <summary>
    /// Refine triage based on critique feedback
    /// </summary>
    public async Task<TriageResult> RefineAsync(
        RunContext context,
        TriageResult previousResult,
        CritiqueResult critiqueFeedback,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetTriageRefinementSchema();
        var categoriesJson = string.Join(", ", PredefinedCategories.Select(c => $"\"{c}\""));

        var issuesText = string.Join("\n", critiqueFeedback.Issues
            .Select(i => $"- [{i.Severity}/5] {i.Category}: {i.Problem} -> {i.Suggestion}"));

        var suggestionsText = string.Join("\n", critiqueFeedback.Suggestions.Take(3));

        // Track in execution state
        if (context.ExecutionState != null)
        {
            context.ExecutionState.TriageScore = critiqueFeedback.Score;
            context.ExecutionState.TriageCritiqueIssues = critiqueFeedback.Issues
                .Select(i => $"{i.Problem}")
                .ToList();
        }

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "triage-refine.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["ISSUE_BODY"] = context.Issue.Body ?? string.Empty,
                ["PREV_CATEGORIES"] = string.Join(", ", previousResult.Categories),
                ["PREV_CONFIDENCE"] = previousResult.ConfidenceScore.ToString("P"),
                ["PREV_CUSTOM_CATEGORY"] = previousResult.CustomCategory?.Name ?? "None",
                ["CRITIQUE_SCORE"] = critiqueFeedback.Score.ToString("0.##"),
                ["CRITIQUE_ISSUES"] = issuesText,
                ["CRITIQUE_SUGGESTIONS"] = suggestionsText
            },
            cancellationToken);

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            },
            JsonSchema = schema,
            SchemaName = "TriageRefinement",
            Temperature = 0.4
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseTriageResponse(response, context, previousResult);
    }

    private TriageResult ParseTriageResponse(LlmResponse response, RunContext context, TriageResult? previous = null)
    {
        var defaultResult = new TriageResult
        {
            Categories = new List<string> { "unclassified" },
            ExtractedDetails = new Dictionary<string, string> { { "title", context.Issue.Title } },
            ConfidenceScore = 0.3m,
            Reasoning = "Failed to parse response",
            CustomCategory = null
        };

        if (!response.IsSuccess)
        {
            return previous ?? defaultResult;
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);

            // Parse categories
            var categories = json.TryGetProperty("categories", out var catProperty)
                ? catProperty.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList()
                : new List<string> { "unclassified" };

            if (categories.Count == 0)
            {
                categories = new List<string> { "unclassified" };
            }

            // Parse custom category
            CustomCategory? customCat = null;
            if (json.TryGetProperty("custom_category", out var customProperty) && customProperty.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                customCat = new CustomCategory
                {
                    Name = customProperty.GetProperty("name").GetString() ?? "",
                    Description = customProperty.GetProperty("description").GetString() ?? "",
                    RequiredFields = customProperty.TryGetProperty("required_fields", out var fieldsProperty)
                        ? fieldsProperty.EnumerateArray()
                            .Select(x => x.GetString() ?? "")
                            .ToList()
                        : new List<string>()
                };
            }

            // Parse extracted details
            var extractedDetails = new Dictionary<string, string>();
            if (json.TryGetProperty("extracted_details", out var detailsProperty))
            {
                foreach (var prop in detailsProperty.EnumerateObject())
                {
                    var value = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
                    extractedDetails[prop.Name] = value;
                }
            }

            var confidence = json.GetProperty("confidence_score").GetDecimal();
            var reasoning = json.GetProperty("reasoning").GetString() ?? "";

            return new TriageResult
            {
                Categories = categories,
                ExtractedDetails = extractedDetails,
                ConfidenceScore = confidence,
                Reasoning = reasoning,
                CustomCategory = customCat,
                UsesCustomCategory = customCat != null && confidence < ConfidenceThreshold
            };
        }
        catch (Exception ex)
        {
            return previous ?? new TriageResult
            {
                Categories = new List<string> { "unclassified" },
                ExtractedDetails = new Dictionary<string, string> { { "title", context.Issue.Title } },
                ConfidenceScore = 0.2m,
                Reasoning = $"Parse error: {ex.Message}",
                CustomCategory = null
            };
        }
    }
}

/// <summary>
/// Result from triage classification and extraction
/// </summary>
public class TriageResult
{
    public List<string> Categories { get; set; } = new();
    public Dictionary<string, string> ExtractedDetails { get; set; } = new();
    public decimal ConfidenceScore { get; set; } // 0-1
    public string Reasoning { get; set; } = "";
    public CustomCategory? CustomCategory { get; set; }
    public bool UsesCustomCategory { get; set; }
}

/// <summary>
/// Custom category when confidence threshold not met
/// </summary>
public class CustomCategory
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("required_fields")]
    public List<string> RequiredFields { get; set; } = new();

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "Issue did not fit predefined categories";
}
