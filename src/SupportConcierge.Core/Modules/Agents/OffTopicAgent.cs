using System.Text.Json;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Prompts;
using SupportConcierge.Core.Modules.Schemas;

namespace SupportConcierge.Core.Modules.Agents;

public sealed class OffTopicAgent
{
    private readonly ILlmClient _llmClient;

    public OffTopicAgent(ILlmClient llmClient)
    {
        _llmClient = llmClient;
    }

    public async Task<OffTopicAssessment> EvaluateAsync(RunContext context, CancellationToken cancellationToken = default)
    {
        var isIssueComment = context.EventName == "issue_comment";
        var isIssueEvent = context.EventName == "issues";
        if (!isIssueComment && !isIssueEvent)
        {
            return new OffTopicAssessment
            {
                OffTopic = false,
                ConfidenceScore = 0,
                Reason = "Unsupported event type",
                SuggestedAction = "continue"
            };
        }

        if (isIssueComment && (context.IncomingComment == null))
        {
            return new OffTopicAssessment
            {
                OffTopic = false,
                ConfidenceScore = 0,
                Reason = "Missing issue comment",
                SuggestedAction = "continue"
            };
        }

        if (isIssueComment && (context.IsDiagnoseCommand || context.IsStopCommand))
        {
            return new OffTopicAssessment
            {
                OffTopic = false,
                ConfidenceScore = 0,
                Reason = "Command detected",
                SuggestedAction = "continue"
            };
        }

        var commentAuthor = isIssueComment
            ? (context.IncomingComment?.User?.Login ?? string.Empty)
            : (context.Issue.User?.Login ?? string.Empty);
        var commentBody = isIssueComment
            ? (context.IncomingComment?.Body ?? string.Empty)
            : (context.Issue.Body ?? string.Empty);

        var schema = OrchestrationSchemas.GetOffTopicSchema();
        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "offtopic-check.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["ISSUE_BODY"] = context.Issue.Body ?? string.Empty,
                ["ISSUE_AUTHOR"] = context.Issue.User?.Login ?? string.Empty,
                ["COMMENT_AUTHOR"] = commentAuthor,
                ["COMMENT_BODY"] = commentBody,
                ["STATE_CATEGORY"] = context.State?.Category ?? string.Empty
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
            SchemaName = "OffTopicDecision",
            Temperature = 0.2
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseResponse(response);
    }

    private static OffTopicAssessment ParseResponse(LlmResponse response)
    {
        if (!response.IsSuccess)
        {
            return new OffTopicAssessment
            {
                OffTopic = false,
                ConfidenceScore = 0,
                Reason = "LLM response failed",
                SuggestedAction = "continue"
            };
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);
            var offTopic = json.TryGetProperty("off_topic", out var offTopicProp) && offTopicProp.GetBoolean();
            var confidence = json.TryGetProperty("confidence_score", out var confProp)
                ? confProp.GetDecimal()
                : 0m;
            var reason = json.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;
            var suggested = json.TryGetProperty("suggested_action", out var actionProp)
                ? actionProp.GetString() ?? string.Empty
                : string.Empty;

            return new OffTopicAssessment
            {
                OffTopic = offTopic,
                ConfidenceScore = confidence,
                Reason = reason,
                SuggestedAction = suggested
            };
        }
        catch (Exception ex)
        {
            return new OffTopicAssessment
            {
                OffTopic = false,
                ConfidenceScore = 0,
                Reason = $"Parse error: {ex.Message}",
                SuggestedAction = "continue"
            };
        }
    }
}

