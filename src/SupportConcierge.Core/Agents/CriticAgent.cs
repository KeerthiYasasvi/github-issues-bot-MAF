using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Prompts;
using SupportConcierge.Core.Schemas;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Agents;

/// <summary>
/// CriticAgent validates quality at each stage of the pipeline.
/// Uses gpt-4o-mini for cost efficiency (evaluation only, not generation).
/// Integrated at 3 stages: Triage, Research, and Response.
/// 
/// Key responsibility: Catch quality issues early rather than wasting downstream effort.
/// </summary>
public class CriticAgent
{
    private readonly ILlmClient _llmClient;
    private readonly SchemaValidator _schemaValidator;

    // Passing thresholds for each stage
    private const decimal TriagePassThreshold = 6m;       // 1-10 scale
    private const decimal ResearchPassThreshold = 5m;      // 1-10 scale
    private const decimal ResponsePassThreshold = 7m;      // 1-10 scale

    public CriticAgent(ILlmClient llmClient, SchemaValidator schemaValidator)
    {
        _llmClient = llmClient;
        _schemaValidator = schemaValidator;
    }

    /// <summary>
    /// Critique triage results (classification + extraction)
    /// Returns feedback for refinement if score < 6
    /// </summary>
    public async Task<CritiqueResult> CritiqueTriageAsync(
        RunContext context,
        CategoryDecision? triageResult,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetCritiqueResultSchema();
        
        var categoryText = triageResult?.Category ?? "unknown";
        var extractedDetailsText = JsonSerializer.Serialize(context.CasePacket.Fields);

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "critic-triage.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["ISSUE_BODY"] = context.Issue.Body ?? string.Empty,
                ["TRIAGE_CATEGORY"] = categoryText,
                ["TRIAGE_CONFIDENCE"] = triageResult?.Confidence.ToString("0.00") ?? "0.00",
                ["EXTRACTED_DETAILS_JSON"] = extractedDetailsText
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
            SchemaName = "CritiqueResult",
            Temperature = 0.3
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseCritiqueResponse(response, "triage");
    }

    /// <summary>
    /// Critique research results (information gathered)
    /// Returns feedback for deeper investigation if score < 5
    /// </summary>
    public async Task<CritiqueResult> CritiqueResearchAsync(
        RunContext context,
        ScoringResult? researchResult,
        List<string> investigationResults,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetCritiqueResultSchema();
        
        var resultsText = string.Join("\n---\n", investigationResults);
        var categoriesText = context.CategoryDecision?.Category ?? "unknown";

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "critic-research.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["CATEGORIES"] = categoriesText,
                ["INVESTIGATION_RESULTS"] = resultsText
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
            SchemaName = "CritiqueResult",
            Temperature = 0.3
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseCritiqueResponse(response, "research");
    }

    /// <summary>
    /// Critique response/brief quality
    /// Returns feedback for refinement if score < 7
    /// </summary>
    public async Task<CritiqueResult> CritiqueResponseAsync(
        RunContext context,
        EngineerBrief? brief,
        FollowUpResponse? followUp,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetCritiqueResultSchema();
        
        var briefText = brief != null ? JsonSerializer.Serialize(brief) : "No brief generated";
        var followUpText = followUp != null ? string.Join("\n", followUp.Questions.Select(q => q.Question).Take(3)) : "No follow-ups";
        var categoryText = context.CategoryDecision?.Category ?? "unknown";

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "critic-response.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["CATEGORY"] = categoryText,
                ["BRIEF_JSON"] = briefText,
                ["FOLLOW_UPS"] = followUpText
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
            SchemaName = "CritiqueResult",
            Temperature = 0.3
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseCritiqueResponse(response, "response");
    }

    private CritiqueResult ParseCritiqueResponse(LlmResponse response, string stage)
    {
        if (!response.IsSuccess)
        {
            LogCritiqueFailure(stage, "LLM call failed", response, null);
            return new CritiqueResult
            {
                Score = 3,
                Reasoning = $"Unable to critique {stage}: LLM error",
                Issues = new List<CritiqueIssue>
                {
                    new()
                    {
                        Category = "system_error",
                        Problem = $"Critique LLM call failed for {stage}",
                        Suggestion = "Retry critique",
                        Severity = 5
                    }
                },
                Suggestions = new List<string> { "Retry the LLM call" },
                IsPassable = false
            };
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);
            
            var issues = json.TryGetProperty("issues", out var issuesProperty)
                ? issuesProperty.EnumerateArray()
                    .Select(x => new CritiqueIssue
                    {
                        Category = x.GetProperty("category").GetString() ?? "unknown",
                        Problem = x.GetProperty("problem").GetString() ?? "",
                        Suggestion = x.GetProperty("suggestion").GetString() ?? "",
                        Severity = (int)x.GetProperty("severity").GetDecimal()
                    })
                    .ToList()
                : new List<CritiqueIssue>();

            var suggestions = json.TryGetProperty("suggestions", out var suggestionsProperty)
                ? suggestionsProperty.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList()
                : new List<string>();

            var score = json.GetProperty("score").GetDecimal();
            var threshold = stage switch
            {
                "triage" => TriagePassThreshold,
                "research" => ResearchPassThreshold,
                "response" => ResponsePassThreshold,
                _ => 5m
            };

            return new CritiqueResult
            {
                Score = score,
                Reasoning = json.GetProperty("reasoning").GetString() ?? "",
                Issues = issues,
                Suggestions = suggestions,
                IsPassable = score >= threshold
            };
        }
        catch (Exception ex)
        {
            LogCritiqueFailure(stage, "Failed to parse critique JSON", response, ex);
            return new CritiqueResult
            {
                Score = 3,
                Reasoning = $"Failed to parse critique response: {ex.Message}",
                Issues = new List<CritiqueIssue>
                {
                    new()
                    {
                        Category = "parse_error",
                        Problem = "Critique response was not valid JSON",
                        Suggestion = "Retry critique",
                        Severity = 4
                    }
                },
                Suggestions = new List<string> { "Retry the critique" },
                IsPassable = false
            };
        }
    }

    private static void LogCritiqueFailure(string stage, string message, LlmResponse response, Exception? exception)
    {
        Console.WriteLine($"[MAF] Critic ({stage}): {message}.");
        if (exception != null)
        {
            Console.WriteLine($"[MAF] Critic ({stage}): Exception: {exception.Message}");
        }

        if (!string.IsNullOrWhiteSpace(response.Content))
        {
            Console.WriteLine($"[MAF] Critic ({stage}): Content: {Truncate(response.Content, 2000)}");
        }

        if (!string.IsNullOrWhiteSpace(response.RawResponse))
        {
            Console.WriteLine($"[MAF] Critic ({stage}): Raw response: {Truncate(response.RawResponse, 2000)}");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength) + "…";
    }
}

/// <summary>
/// Result from critique evaluation
/// </summary>
public class CritiqueResult
{
    [JsonPropertyName("score")]
    public decimal Score { get; set; } // 1-10

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";

    [JsonPropertyName("issues")]
    public List<CritiqueIssue> Issues { get; set; } = new();

    [JsonPropertyName("suggestions")]
    public List<string> Suggestions { get; set; } = new();

    [JsonPropertyName("is_passable")]
    public bool IsPassable { get; set; }
}

/// <summary>
/// Individual issue identified in critique
/// </summary>
public class CritiqueIssue
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = ""; // hallucination, low_confidence, missing_info, unclear, tone, accuracy_error, etc.

    [JsonPropertyName("problem")]
    public string Problem { get; set; } = "";

    [JsonPropertyName("suggestion")]
    public string Suggestion { get; set; } = "";

    [JsonPropertyName("severity")]
    public int Severity { get; set; } // 1-5
}
