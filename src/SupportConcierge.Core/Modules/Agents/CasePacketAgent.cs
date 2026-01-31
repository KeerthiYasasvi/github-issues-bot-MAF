using System.Text.Json;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Core.Modules.Agents;

public sealed class CasePacketAgent
{
    private readonly ILlmClient _llmClient;
    private readonly SchemaValidator _schemaValidator;

    public CasePacketAgent(ILlmClient llmClient, SchemaValidator schemaValidator)
    {
        _llmClient = llmClient;
        _schemaValidator = schemaValidator;
    }

    public async Task<Dictionary<string, string>> ExtractAsync(
        string issueBody,
        string comments,
        List<string> requiredFields,
        CancellationToken cancellationToken = default)
    {
        if (requiredFields.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var schema = BuildSchema(requiredFields);
        var requiredFieldsText = string.Join("\n", requiredFields.Select(f => $"- {f}"));

        var prompt = Prompts.ExtractCasePacket(issueBody, comments, requiredFieldsText);
        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You extract structured fields from GitHub issues." },
                new() { Role = "user", Content = prompt }
            },
            JsonSchema = schema,
            SchemaName = "CasePacket",
            Temperature = 0.0
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        if (!response.IsSuccess)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (!_schemaValidator.TryValidate(response.Content, schema, out _))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in requiredFields)
            {
                if (json.TryGetProperty(field, out var value))
                {
                    results[field] = value.GetString() ?? string.Empty;
                }
            }

            return results;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string BuildSchema(IEnumerable<string> requiredFields)
    {
        var properties = requiredFields.ToDictionary(
            field => field,
            field => new { type = "string" });

        var schema = new
        {
            type = "object",
            properties,
            required = requiredFields.ToArray(),
            additionalProperties = false
        };

        return JsonSerializer.Serialize(schema);
    }
}

