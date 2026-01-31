using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Prompts;
using SupportConcierge.Core.Modules.Schemas;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Core.Modules.Agents;

/// <summary>
/// Enhanced ResponseAgent that generates responses and follow-ups.
/// Follows orchestrator decisions - doesn't decide if follow-ups are needed,
/// but executes what the orchestrator tells it to generate.
/// 
/// Responsibilities:
/// - Generate brief/response based on investigation findings
/// - Generate follow-up questions if orchestrator requests
/// - Respond to critique feedback with refinements
/// - Structure output for posting to GitHub
/// </summary>
public class EnhancedResponseAgent
{
    private const int MaxLogChars = 2000;
    private readonly ILlmClient _llmClient;
    private readonly SchemaValidator _schemaValidator;

    public EnhancedResponseAgent(ILlmClient llmClient, SchemaValidator schemaValidator)
    {
        _llmClient = llmClient;
        _schemaValidator = schemaValidator;
    }

    /// <summary>
    /// Generate response based on investigation findings
    /// </summary>
    public async Task<ResponseGenerationResult> GenerateResponseAsync(
        RunContext context,
        TriageResult triageResult,
        InvestigationResult investigationResult,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetResponseGenerationSchema();

        var categoriesText = string.Join(", ", triageResult.Categories);
        var findingsText = string.Join("\n", investigationResult.Findings
            .Select(f => $"- [{f.FindingType}] {f.Content} (from {f.Source}, confidence: {f.Confidence})"));

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "response-generate.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["ISSUE_BODY"] = context.Issue.Body ?? string.Empty,
                ["CATEGORIES"] = categoriesText,
                ["FINDINGS"] = findingsText
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
            SchemaName = "ResponseGeneration",
            Temperature = 0.5
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseResponseGenerationResponse(response);
    }

    /// <summary>
    /// Generate follow-up questions (if orchestrator requests it)
    /// </summary>
    public async Task<FollowUpResult> GenerateFollowUpAsync(
        RunContext context,
        TriageResult triageResult,
        InvestigationResult investigationResult,
        ResponseGenerationResult previousResponse,
        CancellationToken cancellationToken = default)
    {
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                follow_up_questions = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            question = new { type = "string" },
                            rationale = new { type = "string" },
                            priority = new { type = "string", @enum = new[] { "high", "medium", "low" } }
                        }
                    }
                },
                clarification_needed = new
                {
                    type = "array",
                    items = new { type = "string" }
                },
                additional_context_request = new
                {
                    type = "array",
                    items = new { type = "string" }
                }
            },
            required = new[] { "follow_up_questions", "clarification_needed", "additional_context_request" }
        });

        var categoriesText = string.Join(", ", triageResult.Categories);

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "response-followup.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["CATEGORIES"] = categoriesText,
                ["RESPONSE_TITLE"] = previousResponse.Brief.Title ?? string.Empty,
                ["RESPONSE_SUMMARY"] = previousResponse.Brief.Summary ?? string.Empty
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
            SchemaName = "FollowUpGeneration",
            Temperature = 0.5
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseFollowUpResponse(response);
    }

    /// <summary>
    /// Refine response based on critique feedback
    /// </summary>
    public async Task<ResponseGenerationResult> RefineAsync(
        RunContext context,
        TriageResult triageResult,
        InvestigationResult investigationResult,
        ResponseGenerationResult previousResponse,
        CritiqueResult critiqueFeedback,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetResponseGenerationSchema();

        var issuesText = string.Join("\n", critiqueFeedback.Issues
            .Select(i => $"- [{i.Severity}/5] {i.Category}: {i.Problem} -> {i.Suggestion}"));

        var suggestionsText = string.Join("\n", critiqueFeedback.Suggestions.Take(5));

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "response-refine.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["CATEGORIES"] = string.Join(", ", triageResult.Categories),
                ["PREV_TITLE"] = previousResponse.Brief.Title ?? string.Empty,
                ["PREV_SUMMARY"] = previousResponse.Brief.Summary ?? string.Empty,
                ["PREV_SOLUTION"] = previousResponse.Brief.Solution ?? string.Empty,
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
            SchemaName = "ResponseGeneration",
            Temperature = 0.4
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseResponseGenerationResponse(response);
    }

    private ResponseGenerationResult ParseResponseGenerationResponse(LlmResponse response)
    {
        var defaultResult = new ResponseGenerationResult
        {
            Brief = new BriefResponse
            {
                Title = "Support Response",
                Summary = "Investigation in progress",
                Solution = "Unable to generate response at this time",
                Explanation = "LLM error occurred",
                NextSteps = new List<string> { "Retry response generation" }
            },
            FollowUps = new List<string>(),
            RequiresUserAction = false,
            EscalationNeeded = false
        };

        if (!response.IsSuccess)
        {
            Console.WriteLine("[MAF] Response: LLM call failed or schema validation failed.");
            Console.WriteLine($"[MAF] Response: LLM content (truncated): {TruncateForLog(response.Content)}");
            return defaultResult;
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);

            var briefObj = json.GetProperty("brief");
            var brief = new BriefResponse
            {
                Title = briefObj.GetProperty("title").GetString() ?? "Support Response",
                Summary = briefObj.GetProperty("summary").GetString() ?? "",
                Solution = briefObj.GetProperty("solution").GetString() ?? "",
                Explanation = briefObj.GetProperty("explanation").GetString() ?? "",
                NextSteps = briefObj.TryGetProperty("next_steps", out var nextProperty)
                    ? nextProperty.EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .ToList()
                    : new List<string>()
            };

            var followUps = json.TryGetProperty("follow_ups", out var followProperty)
                ? followProperty.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList()
                : new List<string>();

            return new ResponseGenerationResult
            {
                Brief = brief,
                FollowUps = followUps,
                RequiresUserAction = json.GetProperty("requires_user_action").GetBoolean(),
                EscalationNeeded = json.TryGetProperty("escalation_needed", out var escProp) && escProp.GetBoolean()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] Response: Failed to parse LLM JSON response: {ex.Message}");
            Console.WriteLine($"[MAF] Response: LLM content (truncated): {TruncateForLog(response.Content)}");
            return defaultResult;
        }
    }

    private FollowUpResult ParseFollowUpResponse(LlmResponse response)
    {
        var defaultResult = new FollowUpResult
        {
            FollowUpQuestions = new List<FollowUpQuestion>(),
            ClarificationNeeded = new List<string>(),
            AdditionalContextRequest = new List<string>()
        };

        if (!response.IsSuccess)
        {
            Console.WriteLine("[MAF] FollowUp: LLM call failed or schema validation failed.");
            Console.WriteLine($"[MAF] FollowUp: LLM content (truncated): {TruncateForLog(response.Content)}");
            return defaultResult;
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);

            var questions = json.TryGetProperty("follow_up_questions", out var questionProperty)
                ? questionProperty.EnumerateArray()
                    .Select(q => new FollowUpQuestion
                    {
                        Question = q.GetProperty("question").GetString() ?? "",
                        Rationale = q.GetProperty("rationale").GetString() ?? "",
                        Priority = q.GetProperty("priority").GetString() ?? "medium"
                    })
                    .ToList()
                : new List<FollowUpQuestion>();

            var clarification = json.TryGetProperty("clarification_needed", out var clarProperty)
                ? clarProperty.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList()
                : new List<string>();

            var context = json.TryGetProperty("additional_context_request", out var contextProperty)
                ? contextProperty.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList()
                : new List<string>();

            return new FollowUpResult
            {
                FollowUpQuestions = questions,
                ClarificationNeeded = clarification,
                AdditionalContextRequest = context
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] FollowUp: Failed to parse LLM JSON response: {ex.Message}");
            Console.WriteLine($"[MAF] FollowUp: LLM content (truncated): {TruncateForLog(response.Content)}");
            return defaultResult;
        }
    }

    private static string TruncateForLog(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<empty>";
        }

        return value.Length <= MaxLogChars
            ? value
            : value.Substring(0, MaxLogChars) + "...(truncated)";
    }
}

/// <summary>
/// Result of response generation
/// </summary>
public class ResponseGenerationResult
{
    public BriefResponse Brief { get; set; } = new();
    public List<string> FollowUps { get; set; } = new();
    public bool RequiresUserAction { get; set; }
    public bool EscalationNeeded { get; set; }
}

/// <summary>
/// The actual response brief
/// </summary>
public class BriefResponse
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("solution")]
    public string Solution { get; set; } = "";

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = "";

    [JsonPropertyName("next_steps")]
    public List<string> NextSteps { get; set; } = new();
}

/// <summary>
/// Result of follow-up question generation
/// </summary>
public class FollowUpResult
{
    public List<FollowUpQuestion> FollowUpQuestions { get; set; } = new();
    public List<string> ClarificationNeeded { get; set; } = new();
    public List<string> AdditionalContextRequest { get; set; } = new();
}

/// <summary>
/// Individual follow-up question
/// </summary>
public class FollowUpQuestion
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("rationale")]
    public string Rationale { get; set; } = "";

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium"; // high, medium, low
}

