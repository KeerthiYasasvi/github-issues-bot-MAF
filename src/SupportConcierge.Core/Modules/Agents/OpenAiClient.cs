using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SupportConcierge.Core.Modules.Agents;

public sealed class OpenAiClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly string? _azureApiVersion;

    /// <summary>
    /// Creates an OpenAI client with optional model override.
    /// Falls back to OPENAI_MODEL env var if model is not specified.
    /// Supports dual-model setup via OPENAI_CRITIQUE_MODEL for critic instances.
    /// </summary>
    public OpenAiClient(HttpClient? httpClient = null, string? modelOverride = null)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY not set or empty");
        }

        // Determine which model to use: override > env var
        var model = modelOverride?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            model = Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim();
            if (string.IsNullOrEmpty(model))
            {
                throw new InvalidOperationException("OPENAI_MODEL not set or empty");
            }
        }

        _model = model;
        _httpClient = httpClient ?? new HttpClient();

        var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")?.Trim();
        var azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")?.Trim();
        var azureApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")?.Trim();
        _azureApiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION")?.Trim();

        if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureDeployment) && !string.IsNullOrEmpty(azureApiKey))
        {
            _endpoint = $"{azureEndpoint.TrimEnd('/')}/openai/deployments/{azureDeployment}/chat/completions?api-version={_azureApiVersion ?? "2024-08-01-preview"}";
            _httpClient.DefaultRequestHeaders.Add("api-key", azureApiKey);
        }
        else
        {
            _endpoint = "https://api.openai.com/v1/chat/completions";
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var responseFormat = BuildResponseFormat(request.JsonSchema, request.SchemaName);
        var requestBody = new
        {
            model = _model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            temperature = request.Temperature,
            response_format = responseFormat
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode && request.JsonSchema != null)
        {
            var fallbackBody = new
            {
                model = _model,
                messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                temperature = request.Temperature,
                response_format = new { type = "json_object" }
            };
            var fallbackJson = JsonSerializer.Serialize(fallbackBody);
            using var fallbackContent = new StringContent(fallbackJson, Encoding.UTF8, "application/json");
            response = await _httpClient.PostAsync(_endpoint, fallbackContent, cancellationToken);
            responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            return new LlmResponse
            {
                IsSuccess = false,
                Content = string.Empty,
                RawResponse = responseContent,
                LatencyMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }

        var payload = JsonSerializer.Deserialize<JsonElement>(responseContent);
        var contentText = payload
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        var usage = payload.TryGetProperty("usage", out var usageElement) ? usageElement : default;
        var promptTokens = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
        var completionTokens = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
        var totalTokens = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : 0;

        return new LlmResponse
        {
            IsSuccess = true,
            Content = contentText,
            RawResponse = responseContent,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            LatencyMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    private static object BuildResponseFormat(string? schemaJson, string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return new { type = "json_object" };
        }

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = schemaName,
                schema = JsonSerializer.Deserialize<JsonElement>(schemaJson),
                strict = true
            }
        };
    }
}

