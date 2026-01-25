using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Models;
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

        var prompt = $@"You are a quality evaluator for issue triage. Assess the classification and extraction of this GitHub issue.

Issue Title: {context.Issue.Title}
Issue Body: {context.Issue.Body}

Triage Results:
- Category: {categoryText}
- Confidence: {triageResult?.Confidence:F2}
- Extracted Details: {extractedDetailsText}

Evaluate on these criteria:
1. Accuracy: Is category correctly identified?
2. Completeness: Did extraction capture key problem details?
3. Confidence: How certain are you about these results?
4. Hallucination Risk: Are there unsupported claims?

Score from 1-10 (1=poor, 10=excellent).
Fail if score < 6 (missing info, low confidence, or inaccuracies).

Return JSON with:
- score: 1-10
- reasoning: Brief assessment
- issues: Array of specific problems found
- suggestions: How to improve the triage
- is_passable: Boolean (true if score >= 6)";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are a rigorous quality critic for GitHub issue triage. Identify problems early before downstream processing." },
                new() { Role = "user", Content = prompt }
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

        var prompt = $@"You are a quality evaluator for issue investigation. Assess the depth and relevance of research findings.

Issue Title: {context.Issue.Title}
Issue Categories: {categoriesText}

Investigation Results:
{resultsText}

Evaluate on these criteria:
1. Relevance: Are results directly addressing the issue?
2. Depth: Is investigation thorough enough to support a response?
3. Accuracy: Are findings credible and evidence-based?
4. Completeness: Are there obvious gaps in investigation?
5. Actionability: Can we form a response from these findings?

Score from 1-10 (1=insufficient, 10=excellent).
Fail if score < 5 (missing critical information, speculation, or incomplete).

Return JSON with:
- score: 1-10
- reasoning: Assessment summary
- issues: Array of gaps or concerns
- suggestions: What additional research is needed
- is_passable: Boolean (true if score >= 5)";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are a rigorous quality critic for issue investigation. Ensure findings are thorough before response generation." },
                new() { Role = "user", Content = prompt }
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

        var prompt = $@"You are a quality evaluator for support responses. Assess the generated response to a GitHub issue.

Issue Title: {context.Issue.Title}
Issue Category: {categoryText}

Generated Response:
{briefText}

Follow-up Questions (if any):
{followUpText}

Evaluate on these criteria:
1. Helpfulness: Does the response actually help resolve the issue?
2. Clarity: Is the response clear and easy to understand?
3. Accuracy: Is the information correct and well-founded?
4. Completeness: Does it address the core problem comprehensively?
5. Tone: Is the response professional and empathetic?
6. Actionability: Can the user act on the response?

Score from 1-10 (1=unhelpful, 10=excellent).
Fail if score < 7 (unclear, inaccurate, incomplete, or tone issues).

Return JSON with:
- score: 1-10
- reasoning: Assessment summary
- issues: Array of specific problems with the response
- suggestions: How to improve the response
- is_passable: Boolean (true if score >= 7)";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are a rigorous quality critic for support responses. Ensure responses are helpful, clear, and accurate before posting." },
                new() { Role = "user", Content = prompt }
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
