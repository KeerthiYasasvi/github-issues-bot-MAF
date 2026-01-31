using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Evals;
using SupportConcierge.Core.Guardrails;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.SpecPack;
using SupportConcierge.Core.Tools;
using SupportConcierge.Core.Workflows;

namespace SupportConcierge.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--smoke"))
        {
            return await RunSmokeAsync(args);
        }

        if (args.Contains("--eval"))
        {
            var scenariosDir = GetArgValue(args, "--scenarios-dir") ?? Environment.GetEnvironmentVariable("SUPPORTBOT_EVAL_DIR") ?? "evals/scenarios";
            var outputDir = GetArgValue(args, "--output-dir") ?? "artifacts/evals";
            var subset = GetArgValue(args, "--subset") ?? Environment.GetEnvironmentVariable("SUPPORTBOT_EVAL_SUBSET");
            var useLiveLlm = ParseBool(Environment.GetEnvironmentVariable("SUPPORTBOT_EVAL_USE_LLM"));
            var evalRunner = new SupportConcierge.Cli.Evals.EvalRunner();
            return await evalRunner.RunAllAsync(scenariosDir, outputDir, subset, useLiveLlm);
        }

        var eventFile = GetArgValue(args, "--event-file") ?? Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        if (string.IsNullOrWhiteSpace(eventFile) || !File.Exists(eventFile))
        {
            Console.WriteLine("ERROR: --event-file or GITHUB_EVENT_PATH is required and must exist");
            return 1;
        }

        var dryRun = GetBoolArg(args, "--dry-run") ?? ParseBool(Environment.GetEnvironmentVariable("SUPPORTBOT_DRY_RUN"));
        var writeMode = ParseBool(Environment.GetEnvironmentVariable("SUPPORTBOT_WRITE_MODE"));

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("ERROR: GITHUB_TOKEN is required for runtime execution");
            return 1;
        }

        var eventName = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? GetArgValue(args, "--event-name");

        var json = await File.ReadAllTextAsync(eventFile);
        var root = JsonSerializer.Deserialize<JsonElement>(json);
        var input = ParseEvent(root, eventName);

        var metricsRecord = new MetricsRecord();
        var metrics = new MetricsTool(metricsRecord);

        Console.WriteLine($"[MAF] Runtime config: dry-run={dryRun}, write-mode={writeMode}");

        // Create clients for dual-model setup (agents use gpt-4o, critics use gpt-4o-mini)
        ILlmClient agentLlmClient = CreateLlmClient(metrics, modelOverride: null);  // OPENAI_MODEL (primary)
        ILlmClient criticLlmClient = CreateLlmClient(metrics, modelOverride: Environment.GetEnvironmentVariable("OPENAI_CRITIQUE_MODEL"));  // OPENAI_CRITIQUE_MODEL (optional)

        var schemaValidator = new SchemaValidator();
        var evalDir = Environment.GetEnvironmentVariable("SUPPORTBOT_EVALS_DIR") ?? "artifacts/evals";
        var evalSink = new JsonlEvalSink(evalDir);
        var rubricLoader = new RubricLoader();

        // Initialize new agentic system
        var orchestrator = new OrchestratorAgent(agentLlmClient, schemaValidator);
        var critic = new CriticAgent(criticLlmClient, schemaValidator, rubricLoader, evalSink);
        var triageAgent = new EnhancedTriageAgent(agentLlmClient, schemaValidator);
        var researchAgent = new EnhancedResearchAgent(agentLlmClient, schemaValidator);
        var responseAgent = new EnhancedResponseAgent(agentLlmClient, schemaValidator);
        var casePacketAgent = new CasePacketAgent(agentLlmClient, schemaValidator);
        var offTopicAgent = new OffTopicAgent(agentLlmClient);
        var specPackLoader = new SpecPackLoader();
        var gitHubTool = new GitHubTool(token, dryRun, writeMode);
        var toolRegistry = new ToolRegistry(gitHubTool);

        Console.WriteLine($"[MAF] Building workflow for issue #{input.Issue.Number}: {input.Issue.Title}");

        // Build MAF workflow
        var workflow = SupportConciergeWorkflow.Build(
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

        // Execute workflow
        Console.WriteLine("[MAF] Executing workflow...");
        var workflowRun = await InProcessExecution.RunAsync(workflow, input);

        var resultContext = TryGetRunContext(workflowRun);
        if (resultContext == null && workflowRun != null)
        {
            Console.WriteLine($"[MAF] Unable to extract RunContext from workflow result type: {workflowRun.GetType().FullName}");
        }

        resultContext ??= new RunContext
        {
            Issue = input.Issue,
            Repository = input.Repository
        };

        var decision = ResolveDecision(resultContext);
        if (decision == "unknown")
        {
            Console.WriteLine("[MAF] Final decision unknown; applying fallback follow-up response.");
            ApplyFallbackFollowUp(resultContext);
            decision = ResolveDecision(resultContext);
        }

        // Extract result from workflow execution - the Run object contains execution history
        Console.WriteLine($"\n[MAF Final Decision] {decision}");
        Console.WriteLine($"[Metrics] Tokens used: {metricsRecord.TokenUsage.TotalTokens}");
        Console.WriteLine("[MAF] Workflow completed successfully");

        // NOTE: Removed fallback comment posting - PostCommentExecutor in workflow handles all commenting
        // The duplicate comment bug was caused by posting both in workflow AND here

        await WriteMetricsAsync(metrics);
        await WriteTelemetryAsync(resultContext, metricsRecord);

        var aggregator = new AgentEvalAggregator();
        aggregator.GenerateReports(evalDir);

        return 0;
    }

    private static async Task<int> RunSmokeAsync(string[] args)
    {
        Console.WriteLine("Smoke test not yet updated for agentic system");
        return 0;
    }

    private static ILlmClient CreateLlmClient(MetricsTool metrics)
    {
        return CreateLlmClient(metrics, modelOverride: null, allowMissingConfig: false);
    }

    private static ILlmClient CreateLlmClient(MetricsTool metrics, string? modelOverride = null, bool allowMissingConfig = false)
    {
        var noLlm = ParseBool(Environment.GetEnvironmentVariable("SUPPORTBOT_NO_LLM"));
        ILlmClient inner;
        if (noLlm)
        {
            inner = new NullLlmClient();
        }
        else
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var model = modelOverride ?? Environment.GetEnvironmentVariable("OPENAI_MODEL");
            if (allowMissingConfig && (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model)))
            {
                inner = new NullLlmClient();
            }
            else
            {
                inner = new OpenAiClient(modelOverride: modelOverride);
            }
        }

        return new MetricsLlmClient(inner, response =>
            metrics.AddTokenUsage(response.PromptTokens, response.CompletionTokens, response.TotalTokens, response.LatencyMs));
    }

    private static EventInput ParseEvent(JsonElement root, string? eventName)
    {
        var input = new EventInput
        {
            EventName = eventName,
            Action = root.TryGetProperty("action", out var action) ? action.GetString() : null,
            Issue = root.GetProperty("issue").Deserialize<GitHubIssue>() ?? new GitHubIssue(),
            Repository = root.GetProperty("repository").Deserialize<GitHubRepository>() ?? new GitHubRepository()
        };

        if (root.TryGetProperty("comment", out var comment))
        {
            input.Comment = comment.Deserialize<GitHubComment>();
        }

        return input;
    }

    private static string? GetArgValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1];
        }

        return null;
    }

    private static bool? GetBoolArg(string[] args, string name)
    {
        var value = GetArgValue(args, name);
        if (value == null)
        {
            return null;
        }

        return ParseBool(value);
    }

    private static bool ParseBool(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDecision(RunContext context)
    {
        if (context.ShouldAskFollowUps || context.FollowUpQuestions.Count > 0)
        {
            return "follow_up";
        }
        if (context.ShouldRedirectOffTopic)
        {
            return "off_topic";
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

    private static void ApplyFallbackFollowUp(RunContext context)
    {
        context.ShouldAskFollowUps = true;

        if (context.FollowUpQuestions.Count == 0)
        {
            context.FollowUpQuestions.Add(new SupportConcierge.Core.Models.FollowUpQuestion
            {
                Question = "Please share the exact error message and any relevant logs or stack traces."
            });
            context.FollowUpQuestions.Add(new SupportConcierge.Core.Models.FollowUpQuestion
            {
                Question = "What OS and runtime/build tool versions are you using?"
            });
            context.FollowUpQuestions.Add(new SupportConcierge.Core.Models.FollowUpQuestion
            {
                Question = "What steps lead to the failure?"
            });
        }
    }

    private static async Task TryPostFallbackCommentAsync(RunContext context, IGitHubTool gitHubTool)
    {
        var owner = context.Repository?.Owner?.Login ?? string.Empty;
        var repo = context.Repository?.Name ?? string.Empty;
        var issueNumber = context.Issue?.Number ?? 0;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || issueNumber <= 0)
        {
            Console.WriteLine("[MAF] PostComment: Missing repository or issue information.");
            return;
        }

        var body = ComposeFallbackFollowUp(context);
        if (string.IsNullOrWhiteSpace(body))
        {
            Console.WriteLine("[MAF] PostComment: No content generated; skipping.");
            return;
        }

        var comment = await gitHubTool.PostCommentAsync(owner, repo, issueNumber, body, CancellationToken.None);
        if (comment == null)
        {
            Console.WriteLine("[MAF] PostComment: Skipped (dry-run or write-mode disabled).");
        }
        else
        {
            Console.WriteLine($"[MAF] PostComment: Posted comment id {comment.Id}.");
        }
    }

    private static string ComposeFallbackFollowUp(RunContext context)
    {
        var sb = new System.Text.StringBuilder();
        var author = context.Issue?.User?.Login;
        if (!string.IsNullOrWhiteSpace(author))
        {
            sb.AppendLine($"@{author}");
            sb.AppendLine();
        }

        sb.AppendLine("I need a bit more information to help move this forward:");
        sb.AppendLine();

        var questions = context.FollowUpQuestions
            .Select(q => q.Question)
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Take(3)
            .ToList();

        for (var i = 0; i < questions.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {questions[i]}");
        }

        return sb.ToString().Trim();
    }

    private static RunContext? TryGetRunContext(object? workflowRun)
    {
        if (workflowRun == null)
        {
            return null;
        }

        return ExtractRunContext(workflowRun, maxDepth: 4);
    }

    private static RunContext? ExtractRunContext(object? value, int maxDepth)
    {
        if (value == null)
        {
            return null;
        }

        if (value is RunContext runContext)
        {
            return runContext;
        }

        if (maxDepth <= 0)
        {
            return null;
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
                var fromOutput = ExtractRunContext(output, maxDepth - 1);
                if (fromOutput != null)
                {
                    return fromOutput;
                }
            }
            catch
            {
                // Ignore reflection errors and continue scanning.
            }
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                var fromItem = ExtractRunContext(item, maxDepth - 1);
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
                var fromProperty = ExtractRunContext(propertyValue, maxDepth - 1);
                if (fromProperty != null)
                {
                    return fromProperty;
                }
            }
            catch
            {
                // Ignore properties that throw on access.
            }
        }

        return null;
    }

    private static async Task WriteMetricsAsync(MetricsTool metrics)
    {
        var metricsDir = Environment.GetEnvironmentVariable("SUPPORTBOT_METRICS_DIR") ?? "artifacts/metrics";
        var fileName = $"metrics_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{metrics.Record.RunId}.json";

        try
        {
            await metrics.WriteAsync(metricsDir, fileName);
            Console.WriteLine($"[Metrics] Saved to {metricsDir}/{fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Metrics] Failed to write metrics: {ex.Message}");
        }
    }

    private static async Task WriteTelemetryAsync(RunContext context, MetricsRecord metricsRecord)
    {
        var telemetryDir = Environment.GetEnvironmentVariable("SUPPORTBOT_TELEMETRY_DIR") ?? "artifacts/telemetry";
        var fileName = $"telemetry_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{metricsRecord.RunId}.json";
        var loopCount = context.ActiveUserConversation?.LoopCount
            ?? context.CurrentLoopCount
            ?? context.ExecutionState?.LoopNumber
            ?? 1;
        var commentCount = Math.Max(0, context.PostedCommentCount);
        var totalTokens = metricsRecord.TokenUsage.TotalTokens;
        var tokensPerComment = commentCount > 0 ? totalTokens / (double)commentCount : 0;
        var tokensPerLoop = loopCount > 0 ? totalTokens / (double)loopCount : totalTokens;

        var record = new TelemetryRecord
        {
            RunId = metricsRecord.RunId,
            StartedAt = metricsRecord.StartedAt,
            CompletedAt = metricsRecord.CompletedAt ?? DateTime.UtcNow,
            Repository = context.Repository?.FullName ?? string.Empty,
            IssueNumber = context.Issue?.Number ?? 0,
            IssueTitle = context.Issue?.Title ?? string.Empty,
            EventName = context.EventName ?? string.Empty,
            ActiveUser = context.ActiveParticipant,
            Decision = ResolveDecision(context),
            Category = context.CategoryDecision?.Category ?? "unknown",
            LoopCount = loopCount,
            CommentPosted = context.CommentPosted,
            CommentCount = commentCount,
            LastCommentId = context.LastPostedCommentId,
            TokenUsage = metricsRecord.TokenUsage,
            TokensPerComment = tokensPerComment,
            TokensPerLoop = tokensPerLoop,
            DecisionPath = context.DecisionPath
        };

        try
        {
            Directory.CreateDirectory(telemetryDir);
            var path = Path.Combine(telemetryDir, fileName);
            var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
            Console.WriteLine($"[Telemetry] Saved to {telemetryDir}/{fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telemetry] Failed to write telemetry: {ex.Message}");
        }
    }

    private sealed class TelemetryRecord
    {
        public string RunId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string Repository { get; set; } = string.Empty;
        public int IssueNumber { get; set; }
        public string IssueTitle { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public string ActiveUser { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int LoopCount { get; set; }
        public bool CommentPosted { get; set; }
        public int CommentCount { get; set; }
        public long? LastCommentId { get; set; }
        public TokenUsageSummary TokenUsage { get; set; } = new();
        public double TokensPerComment { get; set; }
        public double TokensPerLoop { get; set; }
        public Dictionary<string, string> DecisionPath { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
