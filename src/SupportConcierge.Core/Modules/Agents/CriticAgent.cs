using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SupportConcierge.Core.Modules.Evals;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Prompts;
using SupportConcierge.Core.Modules.Schemas;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Core.Modules.Agents;

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
    private readonly RubricLoader _rubricLoader;
    private readonly RuleEvaluator _ruleEvaluator;
    private readonly IAgentEvalSink? _evalSink;
    private readonly Random _rng = new();

    // Passing thresholds for each stage
    private const decimal TriagePassThreshold = 6m;       // 1-10 scale
    private const decimal ResearchPassThreshold = 5m;      // 1-10 scale
    private const decimal ResponsePassThreshold = 7m;      // 1-10 scale

    public CriticAgent(ILlmClient llmClient, SchemaValidator schemaValidator, RubricLoader? rubricLoader = null, IAgentEvalSink? evalSink = null)
    {
        _llmClient = llmClient;
        _schemaValidator = schemaValidator;
        _rubricLoader = rubricLoader ?? new RubricLoader();
        _ruleEvaluator = new RuleEvaluator(schemaValidator);
        _evalSink = evalSink;
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
        var evalContext = BuildEvalContext(
            context,
            "triage",
            "Triage",
            $"{context.Issue.Title}\n{context.Issue.Body}",
            JsonSerializer.Serialize(triageResult ?? new CategoryDecision { Category = "unknown", Confidence = 0 })
        );

        var judgement = await EvaluateAsync("Triage", "triage", evalContext, cancellationToken);
        return ToCritiqueResult(judgement, "triage");
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
        var resultsText = string.Join("\n---\n", investigationResults);
        var evalContext = BuildEvalContext(
            context,
            "research",
            "Research",
            $"{context.Issue.Title}\n{context.Issue.Body}",
            resultsText
        );
        evalContext.ToolAllowList = context.ResearchDirective?.AllowedTools?.ToArray() ?? Array.Empty<string>();
        evalContext.ToolsUsed = context.SelectedTools.Select(t => t.ToolName).ToArray();

        var judgement = await EvaluateAsync("Research", "research", evalContext, cancellationToken);
        return ToCritiqueResult(judgement, "research");
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
        var briefText = brief != null ? JsonSerializer.Serialize(brief) : "No brief generated";
        var followUpText = followUp != null ? string.Join("\n", followUp.Questions.Select(q => q.Question).Take(3)) : "No follow-ups";
        var evalContext = BuildEvalContext(
            context,
            "response",
            "Response",
            $"{context.Issue.Title}\n{context.Issue.Body}",
            $"{briefText}\n{followUpText}"
        );
        evalContext.MissingFields = context.Scoring?.MissingFields?.ToList() ?? new List<string>();
        evalContext.FollowUpQuestions = context.FollowUpQuestions.ToList();

        var judgement = await EvaluateAsync("Response", "response", evalContext, cancellationToken);
        return ToCritiqueResult(judgement, "response");
    }

    public async Task<Judgement> EvaluateAsync(
        string agentName,
        string phaseId,
        EvalContext evalContext,
        CancellationToken cancellationToken = default)
    {
        var rubric = _rubricLoader.Load(agentName);
        evalContext.AgentName = agentName;
        evalContext.PhaseId = phaseId;

        var (subscores, issues, suggestions) = _ruleEvaluator.Evaluate(rubric, evalContext);
        var deterministicScore = subscores.Values.Sum();
        var hasJudgeItems = rubric.Items.Any(i => i.Type.Equals("judge", StringComparison.OrdinalIgnoreCase));

        var judgeMode = (Environment.GetEnvironmentVariable("SUPPORTBOT_JUDGE_MODE") ?? "runtime").ToLowerInvariant();
        var samplingRate = double.TryParse(Environment.GetEnvironmentVariable("SUPPORTBOT_JUDGE_SAMPLING_RATE"), out var rate)
            ? Math.Clamp(rate, 0, 1)
            : 1.0;
        var borderline = deterministicScore >= (rubric.ThresholdScore - 1) && deterministicScore <= (rubric.ThresholdScore + 1);

        var shouldRunJudge = hasJudgeItems && (judgeMode == "runtime" || judgeMode == "offline" || borderline);
        if (judgeMode == "runtime" && samplingRate < 1.0 && _rng.NextDouble() > samplingRate)
        {
            shouldRunJudge = false;
        }

        Dictionary<string, double> judgeSubscores = new(StringComparer.OrdinalIgnoreCase);
        List<string> judgeIssues = new();
        List<string> judgeSuggestions = new();
        LlmResponse? judgeResponse = null;

        if (shouldRunJudge && !agentName.Equals("Critique", StringComparison.OrdinalIgnoreCase))
        {
            var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
                "judge-eval.md",
                new Dictionary<string, string>
                {
                    ["AGENT_NAME"] = agentName,
                    ["PHASE_ID"] = phaseId,
                    ["RUBRIC_JSON"] = JsonSerializer.Serialize(rubric),
                    ["INPUT_SNIPPET"] = evalContext.InputText,
                    ["OUTPUT_SNIPPET"] = evalContext.OutputText
                },
                cancellationToken);

            var request = new LlmRequest
            {
                Messages = new List<LlmMessage>
                {
                    new() { Role = "system", Content = systemPrompt },
                    new() { Role = "user", Content = userPrompt }
                },
                JsonSchema = OrchestrationSchemas.GetJudgeResultSchema(),
                SchemaName = "JudgeResult",
                Temperature = 0.2
            };

            judgeResponse = await _llmClient.CompleteAsync(request, cancellationToken);
            if (judgeResponse.IsSuccess)
            {
                TryParseJudgeResponse(judgeResponse.Content, judgeSubscores, judgeIssues, judgeSuggestions);
            }
        }

        foreach (var kvp in judgeSubscores)
        {
            subscores[kvp.Key] = kvp.Value;
        }
        issues.AddRange(judgeIssues);
        suggestions.AddRange(judgeSuggestions);

        var scoreOverall = Math.Clamp(subscores.Values.Sum(), 0, 10);
        var judgement = new Judgement
        {
            AgentName = agentName,
            PhaseId = phaseId,
            RubricId = rubric.RubricId,
            Subscores = subscores,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            FixSuggestions = suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ScoreOverall = scoreOverall,
            PassedThreshold = scoreOverall >= rubric.ThresholdScore
        };

        await WriteEvalRecordAsync(evalContext, judgement, judgeResponse, cancellationToken);

        if (!agentName.Equals("Critique", StringComparison.OrdinalIgnoreCase))
        {
            await WriteCritiqueSelfRecordAsync(evalContext, judgement, cancellationToken);
        }

        return judgement;
    }

    private static void TryParseJudgeResponse(
        string content,
        Dictionary<string, double> subscores,
        List<string> issues,
        List<string> suggestions)
    {
        try
        {
            var clean = ExtractJsonFromResponseStatic(content);
            var json = JsonSerializer.Deserialize<JsonElement>(clean);

            if (json.TryGetProperty("subscores", out var subs))
            {
                foreach (var prop in subs.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var value))
                    {
                        subscores[prop.Name] = value;
                    }
                }
            }

            if (json.TryGetProperty("issues", out var issuesProp))
            {
                issues.AddRange(issuesProp.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
            }

            if (json.TryGetProperty("suggestions", out var suggProp))
            {
                suggestions.AddRange(suggProp.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[MAF] Critic: Failed to parse judge response JSON: {ex.Message}");
            Console.WriteLine($"[MAF] Critic: Content preview: {content?.Substring(0, Math.Min(200, content?.Length ?? 0)) ?? "null"}");
            // Don't throw - just continue with empty subscores/issues/suggestions
            // The deterministic score will be used as fallback
        }
    }

    /// <summary>
    /// Extracts clean JSON from potentially wrapped or markdown-formatted response
    /// Handles cases where LLM returns: ```json\n{...}\n```, extra whitespace, etc.
    /// The OpenAI client already extracts choices[0].message.content, so this function
    /// just needs to clean up any markdown formatting or text wrappers.
    /// </summary>
    private string ExtractJsonFromResponse(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return "{\"score\": 3, \"reasoning\": \"Empty response from LLM\", \"issues\": [], \"suggestions\": [\"Retry critique\"], \"is_passable\": false}";
        }

        // Remove markdown code blocks (```json ... ``` or just ``` ... ```)
        var cleanJson = Regex.Replace(
            rawContent,
            @"^```(?:json)?\s*\n?|\n?```$",
            "",
            RegexOptions.Multiline
        ).Trim();

        // Try to extract JSON object if wrapped in other text
        // This handles cases where LLM adds explanatory text before/after JSON
        var jsonMatch = Regex.Match(
            cleanJson,
            @"\{(?:[^{}]|(?:\{(?:[^{}]|(?:\{[^{}]*\}))*\}))*\}",
            RegexOptions.Singleline
        );

        if (jsonMatch.Success)
        {
            return jsonMatch.Value;
        }

        // If no JSON object found, try parsing the entire cleaned content
        // (LLM might have returned pure JSON without wrappers)
        try
        {
            var testParse = JsonSerializer.Deserialize<JsonElement>(cleanJson);
            if (testParse.ValueKind == JsonValueKind.Object)
            {
                return cleanJson; // It's valid JSON!
            }
        }
        catch
        {
            // Not valid JSON, continue to fallback
        }

        // If we still have content but couldn't parse it, return a valid default structure
        // Log the unparseable content for debugging
        Console.WriteLine($"[MAF] Critic: Could not extract JSON from response, using fallback. Content preview: {cleanJson.Substring(0, Math.Min(200, cleanJson.Length))}");
        return "{\"score\": 3, \"reasoning\": \"Unable to parse structured critique response\", \"issues\": [], \"suggestions\": [\"Retry critique with proper JSON format\"], \"is_passable\": false}";
    }

    private static string ExtractJsonFromResponseStatic(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return "{}";
        }

        var cleanJson = Regex.Replace(
            rawContent,
            @"^```(?:json)?\s*\n?|\n?```$",
            "",
            RegexOptions.Multiline
        ).Trim();

        var jsonMatch = Regex.Match(
            cleanJson,
            @"\{(?:[^{}]|(?:\{(?:[^{}]|(?:\{[^{}]*\}))*\}))*\}",
            RegexOptions.Singleline
        );

        if (jsonMatch.Success)
        {
            return jsonMatch.Value;
        }

        return cleanJson;
    }

    private EvalContext BuildEvalContext(RunContext context, string phaseId, string agentName, string inputText, string outputText)
    {
        var started = DateTimeOffset.UtcNow;
        var ended = DateTimeOffset.UtcNow;

        return new EvalContext
        {
            RunId = BuildRunId(context),
            PhaseId = phaseId,
            AgentName = agentName,
            InputText = inputText ?? string.Empty,
            OutputText = outputText ?? string.Empty,
            StartedAt = started,
            EndedAt = ended
        };
    }

    private static string BuildRunId(RunContext context)
    {
        var repo = context.Repository?.FullName ?? "repo";
        var issue = context.Issue?.Number ?? 0;
        var user = context.ActiveParticipant ?? "user";
        return $"{repo}#{issue}:{user}:{DateTime.UtcNow:yyyyMMddHHmmss}";
    }

    private static CritiqueResult ToCritiqueResult(Judgement judgement, string stage)
    {
        var threshold = stage switch
        {
            "triage" => TriagePassThreshold,
            "research" => ResearchPassThreshold,
            "response" => ResponsePassThreshold,
            _ => 5m
        };

        var issues = judgement.Issues.Select(issue => new CritiqueIssue
        {
            Category = "generic",
            Problem = issue,
            Suggestion = judgement.FixSuggestions.FirstOrDefault() ?? string.Empty,
            Severity = 3
        }).ToList();

        return new CritiqueResult
        {
            Score = (decimal)judgement.ScoreOverall,
            Reasoning = $"Judge rubric {judgement.RubricId} score {judgement.ScoreOverall:0.0}",
            Issues = issues,
            Suggestions = judgement.FixSuggestions,
            IsPassable = judgement.ScoreOverall >= (double)threshold
        };
    }

    private async Task WriteEvalRecordAsync(EvalContext evalContext, Judgement judgement, LlmResponse? judgeResponse, CancellationToken ct)
    {
        if (_evalSink == null)
        {
            return;
        }

        var (inputHash, inputSnippet, inputRedacted) = EvalSanitizer.HashAndSnippet(evalContext.InputText);
        var (outputHash, outputSnippet, outputRedacted) = EvalSanitizer.HashAndSnippet(evalContext.OutputText);

        var record = new AgentEvalRecord
        {
            RunId = evalContext.RunId,
            PhaseId = evalContext.PhaseId,
            AgentName = evalContext.AgentName,
            StartedAt = evalContext.StartedAt,
            EndedAt = evalContext.EndedAt,
            DurationMs = (long)(evalContext.EndedAt - evalContext.StartedAt).TotalMilliseconds,
            InputHash = inputHash,
            OutputHash = outputHash,
            InputSnippet = inputSnippet,
            OutputSnippet = outputSnippet,
            SecretsRedacted = inputRedacted || outputRedacted,
            ToolAllowList = evalContext.ToolAllowList,
            ToolsUsed = evalContext.ToolsUsed,
            Model = evalContext.Model,
            PromptTokens = judgeResponse?.PromptTokens ?? evalContext.PromptTokens,
            CompletionTokens = judgeResponse?.CompletionTokens ?? evalContext.CompletionTokens,
            TotalTokens = judgeResponse?.TotalTokens ?? evalContext.TotalTokens,
            LatencyMs = judgeResponse?.LatencyMs ?? evalContext.LatencyMs,
            Judgement = judgement
        };

        await _evalSink.WriteAsync(record, ct);
    }

    private async Task WriteCritiqueSelfRecordAsync(EvalContext evalContext, Judgement targetJudgement, CancellationToken ct)
    {
        if (_evalSink == null)
        {
            return;
        }

        var rubric = _rubricLoader.Load("Critique");
        var subscores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        var suggestions = new List<string>();

        foreach (var item in rubric.Items.Where(i => i.Type.Equals("rule", StringComparison.OrdinalIgnoreCase)))
        {
            if (item.RuleId == "nonempty_issues" && targetJudgement.Issues.Count == 0)
            {
                subscores[item.Id] = 0;
                issues.Add("Critique provided no issues.");
                suggestions.Add("Provide at least one concrete issue or confirmation.");
            }
            else
            {
                subscores[item.Id] = item.MaxPoints;
            }
        }

        var scoreOverall = Math.Clamp(subscores.Values.Sum(), 0, 10);
        var judgement = new Judgement
        {
            AgentName = "Critique",
            PhaseId = "critique",
            RubricId = rubric.RubricId,
            Subscores = subscores,
            Issues = issues,
            FixSuggestions = suggestions,
            ScoreOverall = scoreOverall,
            PassedThreshold = scoreOverall >= rubric.ThresholdScore
        };

        var record = new AgentEvalRecord
        {
            RunId = evalContext.RunId,
            PhaseId = "critique",
            AgentName = "Critique",
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow,
            DurationMs = 0,
            InputHash = EvalSanitizer.HashString(targetJudgement.AgentName),
            OutputHash = EvalSanitizer.HashString(JsonSerializer.Serialize(targetJudgement)),
            InputSnippet = targetJudgement.AgentName,
            OutputSnippet = $"score={targetJudgement.ScoreOverall:0.0}",
            SecretsRedacted = false,
            ToolAllowList = Array.Empty<string>(),
            ToolsUsed = Array.Empty<string>(),
            Model = evalContext.Model,
            Judgement = judgement
        };

        await _evalSink.WriteAsync(record, ct);
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

