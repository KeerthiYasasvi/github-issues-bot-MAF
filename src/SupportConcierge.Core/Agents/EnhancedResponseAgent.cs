using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Schemas;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Agents;

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

        var prompt = $@"You are generating a support response for a GitHub issue.

Issue Title: {context.Issue.Title}
Issue Body: {context.Issue.Body}
Categories: {categoriesText}

Investigation Findings:
{findingsText}

Generate a comprehensive response that:
1. Acknowledges the issue and its context
2. Explains the root cause based on findings
3. Provides clear solution(s) and next steps
4. Maintains a helpful and professional tone
5. Offers follow-up support if needed

Return JSON with:
- brief: Object containing title, summary, solution, explanation, next_steps
- follow_ups: Array of potential follow-up questions (not decided yet)
- requires_user_action: Boolean - does user need to do something?
- escalation_needed: Boolean - does this need human review?";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are an expert support agent generating clear, helpful responses to GitHub issues." },
                new() { Role = "user", Content = prompt }
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

        var prompt = $@"You are generating follow-up questions for a GitHub issue support case.

Issue Title: {context.Issue.Title}
Categories: {categoriesText}

Our Response:
Title: {previousResponse.Brief.Title}
Summary: {previousResponse.Brief.Summary}

Generate strategic follow-up questions that:
1. Clarify any ambiguities in the original issue
2. Request additional context needed for next steps
3. Help validate that the solution works for the user
4. Prioritize by importance (high/medium/low)

Return JSON with:
- follow_up_questions: Array of questions with rationale and priority
- clarification_needed: What specific clarification do we need?
- additional_context_request: What context would help?";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are expert at generating strategic follow-up questions that move resolution forward." },
                new() { Role = "user", Content = prompt }
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

        var prompt = $@"You are refining a support response based on quality feedback.

Issue Title: {context.Issue.Title}
Categories: {string.Join(", ", triageResult.Categories)}

Previous Response:
Title: {previousResponse.Brief.Title}
Summary: {previousResponse.Brief.Summary}
Solution: {previousResponse.Brief.Solution}

Quality Feedback (Score: {critiqueFeedback.Score}/10):
Issues Identified:
{issuesText}

Improvement Suggestions:
{suggestionsText}

Refine the response addressing all feedback:
1. Fix identified accuracy or clarity issues
2. Incorporate improvement suggestions
3. Maintain professional, helpful tone
4. Ensure next steps are clear and actionable

Return refined JSON with:
- brief: Enhanced brief object with improved title, summary, solution, explanation, next_steps
- follow_ups: Updated follow-up questions if relevant
- requires_user_action: Boolean assessment
- escalation_needed: Boolean assessment";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are expert at refining support responses based on quality feedback." },
                new() { Role = "user", Content = prompt }
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
        catch
        {
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
        catch
        {
            return defaultResult;
        }
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
