using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Evals;
using FollowUpQuestionModel = SupportConcierge.Core.Models.FollowUpQuestion;
using FollowUpQuestionAgent = SupportConcierge.Core.Agents.FollowUpQuestion;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.SpecPack;
using SupportConcierge.Core.Tools;
using SupportConcierge.Core.Workflows;

namespace SupportConcierge.Cli.Evals;

public sealed class EvalRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    private string _outputDir = "artifacts/evals";

    public async Task<int> RunAllAsync(
        string scenariosDir,
        string outputDir,
        string? subsetTag = null,
        bool useLiveLlm = false)
    {
        _outputDir = outputDir;
        var config = LoadEvalConfig(Path.Combine("evals", "eval_config.json"));
        var scenarios = LoadEvalScenarios(scenariosDir, subsetTag);
        var followupScenarios = LoadFollowupScenarios(Path.Combine(scenariosDir, "followups"));
        var briefScenarios = LoadBriefScenarios(Path.Combine(scenariosDir, "briefs"));

        var e2eResults = new List<EvalResult>();
        var followupResults = new List<FollowUpEvalResult>();
        var briefResults = new List<BriefEvalResult>();

        foreach (var scenario in scenarios)
        {
            var result = await RunScenarioAsync(scenario, config, useLiveLlm);
            e2eResults.Add(result);
        }

        foreach (var scenario in followupScenarios)
        {
            var result = await RunFollowupScenarioAsync(scenario, config, useLiveLlm);
            followupResults.Add(result);
        }

        foreach (var scenario in briefScenarios)
        {
            var result = await RunBriefScenarioAsync(scenario, config, useLiveLlm);
            briefResults.Add(result);
        }

        Directory.CreateDirectory(outputDir);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "eval_e2e_results.json"),
            JsonSerializer.Serialize(e2eResults, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "eval_followup_results.json"),
            JsonSerializer.Serialize(followupResults, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "eval_brief_results.json"),
            JsonSerializer.Serialize(briefResults, new JsonSerializerOptions { WriteIndented = true }));

        var summary = BuildSummary(config, e2eResults, followupResults, briefResults);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "eval_summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        var failed = e2eResults.Any(r => !r.Passed)
            || followupResults.Any(r => r.Failed)
            || briefResults.Any(r => r.Failed);

        Console.WriteLine($"[Eval] E2E scenarios: {e2eResults.Count}, followups: {followupResults.Count}, briefs: {briefResults.Count}");
        Console.WriteLine($"[Eval] Overall status: {(failed ? "FAILED" : "PASSED")}");

        return failed ? 1 : 0;
    }

    private static EvalConfig LoadEvalConfig(string path)
    {
        if (!File.Exists(path))
        {
            return new EvalConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EvalConfig>(json, JsonOptions) ?? new EvalConfig();
    }

    private static List<EvalScenario> LoadEvalScenarios(string scenariosDir, string? subsetTag)
    {
        if (!Directory.Exists(scenariosDir))
        {
            return new List<EvalScenario>();
        }

        var files = Directory.GetFiles(scenariosDir, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}followups{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}briefs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var scenarios = files.Select(path =>
            JsonSerializer.Deserialize<EvalScenario>(File.ReadAllText(path), JsonOptions) ?? new EvalScenario())
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToList();

        if (!string.IsNullOrWhiteSpace(subsetTag))
        {
            scenarios = scenarios.Where(s => s.Tags.Any(t => string.Equals(t, subsetTag, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        return scenarios;
    }

    private static List<FollowUpEvalScenario> LoadFollowupScenarios(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return new List<FollowUpEvalScenario>();
        }

        return Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => JsonSerializer.Deserialize<FollowUpEvalScenario>(File.ReadAllText(path), JsonOptions) ?? new FollowUpEvalScenario())
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToList();
    }

    private static List<BriefEvalScenario> LoadBriefScenarios(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return new List<BriefEvalScenario>();
        }

        return Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => JsonSerializer.Deserialize<BriefEvalScenario>(File.ReadAllText(path), JsonOptions) ?? new BriefEvalScenario())
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToList();
    }

    private async Task<EvalResult> RunScenarioAsync(EvalScenario scenario, EvalConfig config, bool useLiveLlm)
    {
        Environment.SetEnvironmentVariable("SUPPORTBOT_DRY_RUN", "true");
        Environment.SetEnvironmentVariable("SUPPORTBOT_WRITE_MODE", "false");

        var metricsRecord = new MetricsRecord();
        var metrics = new MetricsTool(metricsRecord);
        var llmClient = BuildEvalLlmClient(metrics, useLiveLlm);
        var schemaValidator = new SchemaValidator();

        var orchestrator = new OrchestratorAgent(llmClient, schemaValidator);
        var evalDir = _outputDir;
        var evalSink = new SupportConcierge.Core.Evals.JsonlEvalSink(evalDir);
        var critic = new CriticAgent(llmClient, schemaValidator, new SupportConcierge.Core.Evals.RubricLoader(), evalSink);
        var triageAgent = new EnhancedTriageAgent(llmClient, schemaValidator);
        var researchAgent = new EnhancedResearchAgent(llmClient, schemaValidator);
        var responseAgent = new EnhancedResponseAgent(llmClient, schemaValidator);
        var casePacketAgent = new CasePacketAgent(llmClient, schemaValidator);
        var offTopicAgent = new OffTopicAgent(llmClient);

        var comments = scenario.Comments.ToList();
        var gitHubTool = new FakeGitHubTool(comments);
        var toolRegistry = new ToolRegistry(gitHubTool);
        var specPack = LoadSpecPack(scenario.SpecPackPath);
        var specPackLoader = new FakeSpecPackLoader(specPack);

        Workflow BuildWorkflow()
        {
            return SupportConciergeWorkflow.Build(
                triageAgent,
                researchAgent,
                responseAgent,
                critic,
                orchestrator,
                casePacketAgent,
                offTopicAgent,
                toolRegistry,
                specPackLoader,
                gitHubTool);
        }

        RunContext? finalContext = null;

        if (scenario.Events.Count > 0)
        {
            foreach (var evt in scenario.Events)
            {
                var incoming = BuildComment(evt.CommentBody, evt.CommentAuthor);
                if (evt.EventName == "issue_comment")
                {
                    comments.Insert(0, incoming);
                }

                var input = new EventInput
                {
                    EventName = evt.EventName,
                    Issue = scenario.Issue,
                    Repository = scenario.Repository,
                    Comment = evt.EventName == "issue_comment" ? incoming : null
                };

                var run = await InProcessExecution.RunAsync(BuildWorkflow(), input);
                finalContext = ExtractRunContext(run) ?? finalContext;
            }
        }
        else
        {
            var input = new EventInput
            {
                EventName = scenario.EventName,
                Issue = scenario.Issue,
                Repository = scenario.Repository,
                Comment = scenario.EventName == "issue_comment" ? scenario.Comments.LastOrDefault() : null
            };
            var run = await InProcessExecution.RunAsync(BuildWorkflow(), input);
            finalContext = ExtractRunContext(run);
        }

        finalContext ??= new RunContext { Issue = scenario.Issue, Repository = scenario.Repository };

        var decision = ResolveDecision(finalContext);
        var normalizedCategory = NormalizeCategory(finalContext.CategoryDecision?.Category ?? string.Empty);
        var followUps = CollectFollowUps(finalContext);

        var result = new EvalResult
        {
            ScenarioName = scenario.Name,
            DecisionPath = string.Join(";", finalContext.DecisionPath.Select(kv => $"{kv.Key}={kv.Value}")),
            Category = normalizedCategory,
            Confidence = finalContext.CategoryDecision?.Confidence ?? 0,
            CompletenessScore = finalContext.Scoring?.Score ?? 0,
            MissingFields = finalContext.Scoring?.MissingFields ?? new List<string>(),
            FollowUps = followUps,
            Brief = finalContext.Brief,
            TokenUsage = metricsRecord.TokenUsage,
            TotalLatencyMs = metricsRecord.TokenUsage.TotalLatencyMs
        };

        ApplyExpectations(scenario, finalContext, decision, normalizedCategory, followUps, config, result);
        return result;
    }

    private async Task<FollowUpEvalResult> RunFollowupScenarioAsync(FollowUpEvalScenario scenario, EvalConfig config, bool useLiveLlm)
    {
        var metricsRecord = new MetricsRecord();
        var metrics = new MetricsTool(metricsRecord);
        var llmClient = BuildEvalLlmClient(metrics, useLiveLlm);
        var schemaValidator = new SchemaValidator();
        var responseAgent = new EnhancedResponseAgent(llmClient, schemaValidator);

        var context = new RunContext
        {
            Issue = new GitHubIssue { Title = scenario.Name, Body = scenario.IssueBody },
            CategoryDecision = new CategoryDecision { Category = scenario.Category }
        };

        var triageResult = new TriageResult { Categories = new List<string> { scenario.Category } };
        var investigationResult = new InvestigationResult();
        var priorResponse = new ResponseGenerationResult
        {
            Brief = new BriefResponse { Title = "Initial response", Summary = "Needs more info", NextSteps = new List<string>() }
        };

        var followUpResult = await responseAgent.GenerateFollowUpAsync(context, triageResult, investigationResult, priorResponse);
        var questions = followUpResult.FollowUpQuestions;
        var questionModels = questions
            .Select(q => new FollowUpQuestionModel { Question = q.Question, WhyNeeded = q.Rationale })
            .ToList();
        var score = ScoreFollowUps(scenario.MissingFields, scenario.AskedBefore, questionModels, out var notes);

        var requiredMin = Math.Min(config.MinFollowUpScore, scenario.MissingFields.Count);
        var failed = score < requiredMin;
        return new FollowUpEvalResult
        {
            ScenarioName = scenario.Name,
            Questions = questionModels,
            Score = score,
            Failed = failed,
            Notes = notes
        };
    }

    private async Task<BriefEvalResult> RunBriefScenarioAsync(BriefEvalScenario scenario, EvalConfig config, bool useLiveLlm)
    {
        var metricsRecord = new MetricsRecord();
        var metrics = new MetricsTool(metricsRecord);
        var llmClient = BuildEvalLlmClient(metrics, useLiveLlm);
        var schemaValidator = new SchemaValidator();

        var prompt = Prompts.GenerateEngineerBrief(
            scenario.IssueBody,
            string.Empty,
            scenario.Category,
            scenario.ExtractedFields,
            scenario.Playbook,
            scenario.RepoDocs,
            string.Empty);

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You generate engineer briefs in JSON." },
                new() { Role = "user", Content = prompt }
            },
            JsonSchema = Schemas.EngineerBriefSchema,
            SchemaName = "EngineerBrief",
            Temperature = 0.4
        };

        var response = await llmClient.CompleteAsync(request);
        var brief = ParseEngineerBrief(response.Content, schemaValidator);
        var score = ScoreBrief(brief, out var notes);

        var failed = score < config.MinBriefScore;
        return new BriefEvalResult
        {
            ScenarioName = scenario.Name,
            Brief = brief,
            Score = score,
            Failed = failed,
            Notes = notes
        };
    }

    private static ILlmClient BuildEvalLlmClient(MetricsTool metrics, bool useLiveLlm)
    {
        ILlmClient inner = useLiveLlm ? new OpenAiClient() : new HeuristicLlmClient();
        return new MetricsLlmClient(inner, response =>
            metrics.AddTokenUsage(response.PromptTokens, response.CompletionTokens, response.TotalTokens, response.LatencyMs));
    }

    private static SpecPackConfig LoadSpecPack(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new SpecPackConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SpecPackConfig>(json, JsonOptions) ?? new SpecPackConfig();
    }

    private static GitHubComment BuildComment(string body, string author)
    {
        return new GitHubComment
        {
            Body = body,
            User = new GitHubUser { Login = string.IsNullOrWhiteSpace(author) ? "user" : author },
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string ResolveDecision(RunContext context)
    {
        if (context.ShouldRedirectOffTopic)
        {
            return "off_topic";
        }
        if (context.ShouldAskFollowUps || context.FollowUpQuestions.Count > 0)
        {
            return "follow_up";
        }
        if (context.ShouldEscalate)
        {
            return "escalate";
        }
        if (context.ShouldStop)
        {
            return "stop";
        }
        if (context.ShouldFinalize || context.Brief != null)
        {
            return "finalize";
        }

        return "unknown";
    }

    private static string NormalizeCategory(string category)
    {
        var lower = category.ToLowerInvariant();
        return lower switch
        {
            var c when c.Contains("documentation") || c.Contains("doc") => "docs",
            var c when c.Contains("runtime") => "runtime",
            var c when c.Contains("build") => "build",
            var c when c.Contains("environment") || c.Contains("setup") => "setup",
            var c when c.Contains("feature") => "feature",
            var c when c.Contains("config") => "configuration",
            var c when c.Contains("bug") => "bug",
            _ => lower
        };
    }

    private static List<FollowUpQuestionModel> CollectFollowUps(RunContext context)
    {
        var list = new List<FollowUpQuestionModel>();
        if (context.FollowUpQuestions.Count > 0)
        {
            list.AddRange(context.FollowUpQuestions);
        }
        else if (context.ResponseResult?.FollowUps?.Count > 0)
        {
            list.AddRange(context.ResponseResult.FollowUps.Select(q => new FollowUpQuestionModel { Question = q }));
        }
        return list;
    }

    private static void ApplyExpectations(
        EvalScenario scenario,
        RunContext context,
        string decision,
        string category,
        List<FollowUpQuestionModel> followUps,
        EvalConfig config,
        EvalResult result)
    {
        var failures = new List<string>();
        var expectations = scenario.Expectations;

        if (expectations?.ExpectedDecision != null && !string.Equals(expectations.ExpectedDecision, decision, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Decision {decision} != expected {expectations.ExpectedDecision}");
        }

        if (expectations?.ExpectedCategory != null && !string.Equals(expectations.ExpectedCategory, category, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Category {category} != expected {expectations.ExpectedCategory}");
        }

        var completeness = result.CompletenessScore;
        if (expectations?.MinCompleteness.HasValue == true && completeness < expectations.MinCompleteness.Value)
        {
            failures.Add($"Completeness {completeness} < min {expectations.MinCompleteness.Value}");
        }
        if (expectations?.MaxCompleteness.HasValue == true && completeness > expectations.MaxCompleteness.Value)
        {
            failures.Add($"Completeness {completeness} > max {expectations.MaxCompleteness.Value}");
        }

        if (expectations?.MinFollowUpQuestions.HasValue == true && followUps.Count < expectations.MinFollowUpQuestions.Value)
        {
            failures.Add($"Follow-ups {followUps.Count} < min {expectations.MinFollowUpQuestions.Value}");
        }
        if (expectations?.MaxFollowUpQuestions.HasValue == true && followUps.Count > expectations.MaxFollowUpQuestions.Value)
        {
            failures.Add($"Follow-ups {followUps.Count} > max {expectations.MaxFollowUpQuestions.Value}");
        }

        if (expectations?.RequiredQuestionKeywords?.Count > 0)
        {
            foreach (var keyword in expectations.RequiredQuestionKeywords)
            {
                if (!followUps.Any(q => q.Question.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"Missing follow-up keyword: {keyword}");
                }
            }
        }

        if (expectations?.ForbiddenQuestionKeywords?.Count > 0)
        {
            foreach (var keyword in expectations.ForbiddenQuestionKeywords)
            {
                if (followUps.Any(q => q.Question.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"Forbidden keyword in follow-ups: {keyword}");
                }
            }
        }

        if (expectations?.ExpectedTools?.Count > 0)
        {
            var toolNames = context.SelectedTools.Select(t => t.ToolName).ToList();
            foreach (var expected in expectations.ExpectedTools)
            {
                if (!toolNames.Any(t => t.Equals(expected, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"Expected tool not selected: {expected}");
                }
            }
        }

        if (expectations?.ExpectOffTopicRedirect.HasValue == true && context.ShouldRedirectOffTopic != expectations.ExpectOffTopicRedirect.Value)
        {
            failures.Add($"Off-topic redirect {context.ShouldRedirectOffTopic} != expected {expectations.ExpectOffTopicRedirect}");
        }

        if (expectations?.ExpectNoSecretsRequested == true && FollowUpsContainSecrets(followUps))
        {
            failures.Add("Follow-ups appear to request secrets");
        }

        if (expectations?.ExpectTriageRefinement == true && !context.TriageRefined)
        {
            failures.Add("Expected triage refinement but none occurred");
        }
        if (expectations?.ExpectResearchDeepDive == true && !context.ResearchDeepDived)
        {
            failures.Add("Expected research deep dive but none occurred");
        }
        if (expectations?.ExpectResponseRefinement == true && !context.ResponseRefined)
        {
            failures.Add("Expected response refinement but none occurred");
        }
        if (expectations?.ExpectInfoSufficient.HasValue == true)
        {
            var value = context.DecisionPath.TryGetValue("info_sufficiency", out var info) ? info : "false";
            var isSufficient = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            if (isSufficient != expectations.ExpectInfoSufficient.Value)
            {
                failures.Add($"Info sufficiency {isSufficient} != expected {expectations.ExpectInfoSufficient}");
            }
        }

        if (expectations?.ExpectedUserLoopCounts?.Count > 0 && context.State?.UserConversations != null)
        {
            foreach (var kvp in expectations.ExpectedUserLoopCounts)
            {
                if (!context.State.UserConversations.TryGetValue(kvp.Key, out var conv) || conv.LoopCount != kvp.Value)
                {
                    failures.Add($"User {kvp.Key} loop count mismatch");
                }
            }
        }

        if (expectations?.ExpectedAllowedUsers?.Count > 0 && context.State?.UserConversations != null)
        {
            foreach (var user in expectations.ExpectedAllowedUsers)
            {
                if (!context.State.UserConversations.ContainsKey(user))
                {
                    failures.Add($"Expected allowed user missing: {user}");
                }
            }
        }

        if (expectations?.MaxLatencyMs.HasValue == true && result.TotalLatencyMs > expectations.MaxLatencyMs.Value)
        {
            failures.Add($"Latency {result.TotalLatencyMs}ms > max {expectations.MaxLatencyMs.Value}ms");
        }
        if (expectations?.MaxTotalTokens.HasValue == true && result.TokenUsage.TotalTokens > expectations.MaxTotalTokens.Value)
        {
            failures.Add($"Tokens {result.TokenUsage.TotalTokens} > max {expectations.MaxTotalTokens.Value}");
        }

        if (config.FailOnHallucinations && result.HallucinationWarnings.Count > 0)
        {
            failures.Add("Hallucination warnings present");
        }

        result.Passed = failures.Count == 0;
        result.FailureReasons = failures;
    }

    private static bool FollowUpsContainSecrets(List<FollowUpQuestionModel> questions)
    {
        var secretKeywords = new[] { "password", "api key", "apikey", "token", "secret", "credential" };
        var requestVerbs = new[] { "share", "paste", "provide", "send", "give me", "post" };
        return questions.Any(q =>
        {
            var text = q.Question.ToLowerInvariant();
            var mentionsSecret = secretKeywords.Any(k => text.Contains(k));
            var asksForValue = requestVerbs.Any(v => text.Contains(v));
            return mentionsSecret && asksForValue;
        });
    }

    private static int ScoreFollowUps(List<string> missingFields, List<string> askedBefore, List<FollowUpQuestionModel> questions, out List<string> notes)
    {
        var score = 0;
        notes = new List<string>();
        var keywords = BuildFieldKeywordMap();

        foreach (var field in missingFields)
        {
            if (!keywords.TryGetValue(field, out var fieldKeywords))
            {
                continue;
            }

            if (questions.Any(q => fieldKeywords.Any(k => q.Question.Contains(k, StringComparison.OrdinalIgnoreCase))))
            {
                score += 1;
            }
            else
            {
                notes.Add($"Missing coverage for field: {field}");
            }
        }

        foreach (var asked in askedBefore)
        {
            if (questions.Any(q => q.Question.Contains(asked, StringComparison.OrdinalIgnoreCase)))
            {
                score -= 1;
                notes.Add($"Repeated previously asked field: {asked}");
            }
        }

        if (questions.Count > 3)
        {
            notes.Add("More than 3 questions generated");
        }

        if (FollowUpsContainSecrets(questions))
        {
            notes.Add("Potential secret request detected");
            score -= 1;
        }

        return Math.Max(0, score);
    }

    private static int ScoreBrief(EngineerBrief? brief, out List<string> notes)
    {
        notes = new List<string>();
        var score = 0;
        if (brief == null)
        {
            notes.Add("No brief generated");
            return score;
        }

        if (!string.IsNullOrWhiteSpace(brief.Summary))
        {
            score++;
        }
        else
        {
            notes.Add("Missing summary");
        }

        if (brief.NextSteps.Count > 0)
        {
            score++;
        }
        else
        {
            notes.Add("Missing next steps");
        }

        if (brief.KeyEvidence.Count > 0)
        {
            score++;
        }
        else
        {
            notes.Add("Missing key evidence");
        }

        return score;
    }

    private static Dictionary<string, List<string>> BuildFieldKeywordMap()
    {
        return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc_location"] = new() { "file", "section", "readme", "location", "page" },
            ["issue_description"] = new() { "missing", "incorrect", "problem", "issue" },
            ["operating_system"] = new() { "os", "operating system", "windows", "linux", "mac" },
            ["runtime_version"] = new() { "runtime", "version", "sdk" },
            ["error_message"] = new() { "error", "exception", "stack trace" },
            ["stack_trace"] = new() { "stack trace", "stacktrace", "trace" },
            ["steps_to_reproduce"] = new() { "steps", "reproduce", "how to" },
            ["api_key"] = new() { "api key", "apikey", "credential", "configured" }
        };
    }

    private static EngineerBrief? ParseEngineerBrief(string content, SchemaValidator schemaValidator)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (!schemaValidator.TryValidate(content, Schemas.EngineerBriefSchema, out _))
        {
            return null;
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(content);
            var brief = new EngineerBrief
            {
                Summary = json.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : string.Empty,
                Symptoms = ReadStringArray(json, "symptoms"),
                ReproSteps = ReadStringArray(json, "repro_steps"),
                Environment = ReadStringDictionary(json, "environment"),
                KeyEvidence = ReadStringArray(json, "key_evidence"),
                NextSteps = ReadStringArray(json, "next_steps"),
                ValidationConfirmations = ReadStringArray(json, "validation_confirmations"),
                PossibleDuplicates = ReadDuplicateReferences(json, "possible_duplicates")
            };
            return brief;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadStringArray(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return prop.EnumerateArray()
            .Select(x => x.GetString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static Dictionary<string, string> ReadStringDictionary(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in prop.EnumerateObject())
        {
            dict[item.Name] = item.Value.GetString() ?? string.Empty;
        }
        return dict;
    }

    private static List<DuplicateReference> ReadDuplicateReferences(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return new List<DuplicateReference>();
        }

        var list = new List<DuplicateReference>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var issueNumber = item.TryGetProperty("issue_number", out var numberProp) ? numberProp.GetInt32() : 0;
            var similarity = item.TryGetProperty("similarity_reason", out var reasonProp) ? reasonProp.GetString() ?? string.Empty : string.Empty;
            if (issueNumber > 0)
            {
                list.Add(new DuplicateReference { IssueNumber = issueNumber, SimilarityReason = similarity });
            }
        }
        return list;
    }

    private static object BuildSummary(
        EvalConfig config,
        List<EvalResult> e2eResults,
        List<FollowUpEvalResult> followupResults,
        List<BriefEvalResult> briefResults)
    {
        var totalTokens = e2eResults.Sum(r => r.TokenUsage.TotalTokens);
        var totalLatency = e2eResults.Sum(r => r.TotalLatencyMs);
        var avgTokens = e2eResults.Count == 0 ? 0 : totalTokens / e2eResults.Count;
        var avgLatency = e2eResults.Count == 0 ? 0 : totalLatency / e2eResults.Count;

        return new
        {
            e2e_total = e2eResults.Count,
            e2e_passed = e2eResults.Count(r => r.Passed),
            followup_total = followupResults.Count,
            followup_passed = followupResults.Count(r => !r.Failed),
            brief_total = briefResults.Count,
            brief_passed = briefResults.Count(r => !r.Failed),
            avg_tokens = avgTokens,
            avg_latency_ms = avgLatency,
            max_avg_tokens = config.MaxAverageTokens,
            max_avg_latency_ms = config.MaxAverageLatencyMs
        };
    }

    private static RunContext? ExtractRunContext(object? workflowRun)
    {
        if (workflowRun == null)
        {
            return null;
        }

        return ExtractRunContextRecursive(workflowRun, maxDepth: 4);
    }

    private static RunContext? ExtractRunContextRecursive(object? value, int maxDepth)
    {
        if (value == null || maxDepth <= 0)
        {
            return null;
        }

        if (value is RunContext runContext)
        {
            return runContext;
        }

        var type = value.GetType();
        var outputProperty = type.GetProperty("Output")
            ?? type.GetProperty("Result")
            ?? type.GetProperty("OutputValue")
            ?? type.GetProperty("Outputs")
            ?? type.GetProperty("Value")
            ?? type.GetProperty("ValueAsObject")
            ?? type.GetProperty("Context")
            ?? type.GetProperty("State")
            ?? type.GetProperty("Data")
            ?? type.GetProperty("Item")
            ?? type.GetProperty("ResultContext");
        if (outputProperty != null)
        {
            try
            {
                var output = outputProperty.GetValue(value);
                var fromOutput = ExtractRunContextRecursive(output, maxDepth - 1);
                if (fromOutput != null)
                {
                    return fromOutput;
                }
            }
            catch
            {
                // Ignore reflection errors
            }
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                var fromItem = ExtractRunContextRecursive(item, maxDepth - 1);
                if (fromItem != null)
                {
                    return fromItem;
                }
            }
        }

        foreach (var property in type.GetProperties())
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                var propertyValue = property.GetValue(value);
                var fromProperty = ExtractRunContextRecursive(propertyValue, maxDepth - 1);
                if (fromProperty != null)
                {
                    return fromProperty;
                }
            }
            catch
            {
                // Ignore properties that throw on access
            }
        }

        return null;
    }
}
