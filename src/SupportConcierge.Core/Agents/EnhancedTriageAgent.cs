using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Models;
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
        var schema = OrchestrationSchemas.GetTriageRefinementSchema();
        var categoriesJson = string.Join(", ", PredefinedCategories.Select(c => $"\"{c}\""));

        var prompt = $@"You are an expert at classifying and analyzing GitHub issues.

Issue Title: {context.Issue.Title}
Issue Body: {context.Issue.Body}

Analyze this issue and provide:
1. Primary category (choose from: {categoriesJson} or suggest custom if none fit)
2. Any secondary categories that apply
3. Extracted key details (error messages, versions, environment info, etc.)
4. Your confidence score (0-1) that these categories are correct

If confidence < 0.75 due to unclear requirements, suggest a custom category with:
- Custom category name (if applicable)
- Description of what makes it unique
- Required fields for proper handling

Return structured JSON with:
- categories: Array of applicable categories
- custom_category: Optional object if creating new category (null if using predefined)
- extracted_details: Object with key details found
- confidence_score: 0-1
- reasoning: Why these categories were chosen";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are an expert GitHub issue classifier. Be precise in categorization and confident in your choices, or declare uncertainty with custom categories." },
                new() { Role = "user", Content = prompt }
            },
            JsonSchema = schema,
            SchemaName = "TriageRefinement",
            Temperature = 0.3
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseTriageResponse(response, context);
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

        var prompt = $@"You are refining GitHub issue triage based on quality feedback.

Issue Title: {context.Issue.Title}
Issue Body: {context.Issue.Body}

Previous Classification:
- Categories: {string.Join(", ", previousResult.Categories)}
- Confidence: {previousResult.ConfidenceScore:P}
- Custom Category: {(previousResult.CustomCategory?.Name ?? "None")}

Quality Feedback (Score: {critiqueFeedback.Score}/10):
Issues Found:
{issuesText}

Suggestions:
{suggestionsText}

Refine the classification addressing the feedback:
1. Keep categories accurate to the core issue
2. If previous confidence was low, consider custom category approach
3. Extract more complete details if missing
4. Improve confidence assessment

Return refined JSON with:
- categories: Refined category list
- custom_category: Updated or new custom category (null if using predefined)
- extracted_details: More complete extraction
- confidence_score: Updated confidence (likely higher after refinement)
- reasoning: What was adjusted and why";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are an expert at refining issue classification based on quality feedback. Address specific concerns raised." },
                new() { Role = "user", Content = prompt }
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
