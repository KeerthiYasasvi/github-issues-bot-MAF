using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Prompts;
using SupportConcierge.Core.Modules.Schemas;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Core.Modules.Agents;

/// <summary>
/// OrchestratorAgent serves as the planner and decision maker for the agentic system.
/// It orchestrates the workflow with clear loops (max 3 iterations) and manages escalation.
/// 
/// Responsibilities:
/// - Understand the task/issue
/// - Create/update plan based on feedback
/// - Execute next steps by delegating to specialist agents
/// - Evaluate progress toward resolution
/// - Replan if needed or escalate after 3 loops
/// </summary>
public class OrchestratorAgent
{
    private readonly ILlmClient _llmClient;
    private readonly SchemaValidator _schemaValidator;
    private const int MaxLoops = 4;

    public OrchestratorAgent(ILlmClient llmClient, SchemaValidator schemaValidator)
    {
        _llmClient = llmClient;
        _schemaValidator = schemaValidator;
    }

    /// <summary>
    /// Initial task understanding: analyze issue and create action plan
    /// </summary>
    public async Task<OrchestratorPlan> UnderstandTaskAsync(RunContext context, CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetPlanSchema();
        
        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "orchestrator-plan.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["ISSUE_BODY"] = context.Issue.Body ?? string.Empty
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
            SchemaName = "OrchestratorPlan",
            Temperature = 0.3
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        if (!response.IsSuccess)
        {
            return new OrchestratorPlan
            {
                ProblemSummary = context.Issue.Title,
                InformationNeeded = new List<string> { "Basic issue details" },
                InvestigationSteps = new List<string> { "Analyze issue body", "Request clarification" },
                LikelyResolution = false,
                Reasoning = "Unable to create plan due to LLM error"
            };
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);
            return new OrchestratorPlan
            {
                ProblemSummary = json.GetProperty("problem_summary").GetString() ?? "Unknown",
                InformationNeeded = json.GetProperty("information_needed")
                    .EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList(),
                InvestigationSteps = json.GetProperty("investigation_steps")
                    .EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList(),
                LikelyResolution = json.GetProperty("likely_resolution").GetBoolean(),
                Reasoning = json.GetProperty("reasoning").GetString() ?? ""
            };
        }
        catch
        {
            return new OrchestratorPlan
            {
                ProblemSummary = context.Issue.Title,
                InformationNeeded = new List<string> { "Basic issue details" },
                InvestigationSteps = new List<string> { "Analyze issue body", "Request clarification" },
                LikelyResolution = false,
                Reasoning = "Plan parsing failed"
            };
        }
    }

    /// <summary>
    /// Decide next step based on current state and progress
    /// FIXED: Now properly implements the 3-loop user interaction pattern:
    /// Loop 1: Ask questions with initial triage/research
    /// Loop 2: With user answers, provide response or ask more questions
    /// Loop 3: With user answers, provide response or ask more questions (up to 9 total questions)
    /// Loop 4+: Escalate to human (user exhausted after 3 full loops)
    /// </summary>
    public async Task<OrchestratorDecision> EvaluateProgressAsync(
        RunContext context,
        OrchestratorPlan plan,
        int currentLoop,
        List<OrchestratorDecision> previousDecisions,
        CancellationToken cancellationToken = default)
    {
        // Check if user has exhausted loops (3 max loops means escalate at loop 4)
        const int maxUserLoops = 3;
        
        if (context.ExecutionState != null)
        {
            context.ExecutionState.LoopNumber = currentLoop;
            context.ExecutionState.TotalUserLoops = maxUserLoops;
        }

        // User has completed 3 loops - escalate
        if (currentLoop > maxUserLoops)
        {
            if (context.ExecutionState != null)
            {
                context.ExecutionState.IsUserExhausted = true;
                context.ExecutionState.LoopActionTaken = "escalate";
            }

            return new OrchestratorDecision
            {
                Action = "escalate",
                Reasoning = $"User has had {maxUserLoops} interactions. Escalating to human review.",
                ConfidenceScore = 0.5m,
                NextAgent = "human"
            };
        }

        // Check if user has self-resolved (indicated they know the fix)
        var selfResolved = DetectSelfResolution(context);
        if (selfResolved)
        {
            Console.WriteLine("[MAF] Orchestrator: User appears to have self-resolved. Providing confirmation response.");
            if (context.ExecutionState != null)
            {
                context.ExecutionState.LoopActionTaken = "confirm_self_resolution";
            }
            return new OrchestratorDecision
            {
                Action = "finalize",
                Reasoning = "User indicated they know the solution or will try a fix. Providing confirmation.",
                ConfidenceScore = 0.9m,
                NextAgent = "response_agent"
            };
        }

        // Check if resolution is achievable with current information
        var infoSufficiency = await AssessInformationSufficiencyAsync(context, plan, cancellationToken);
        
        // IMPORTANT: Trust the LLM's has_enough_info decision
        // The previous logic used deterministicEnough (IsActionable || Fields.Count > 0) to OVERRIDE
        // the LLM's decision, but having extracted fields doesn't mean we have ENOUGH info.
        // The LLM considers the full context when deciding if more questions are needed.
        var hasEnoughInfo = infoSufficiency.HasEnoughInfo;

        context.DecisionPath["info_sufficiency"] = hasEnoughInfo ? "true" : "false";
        Console.WriteLine($"[MAF] Orchestrator: Sufficiency has_enough_info={infoSufficiency.HasEnoughInfo}");
        if (infoSufficiency.MissingInfo.Count > 0)
        {
            Console.WriteLine($"[MAF] Orchestrator: Missing info = {string.Join(", ", infoSufficiency.MissingInfo.Take(4))}");
        }
        if (!string.IsNullOrWhiteSpace(infoSufficiency.Reasoning))
        {
            Console.WriteLine($"[MAF] Orchestrator: Sufficiency reasoning = {Truncate(infoSufficiency.Reasoning, 200)}");
        }

        // Evaluate based on loop stage
        // IMPORTANT: Trust the LLM's has_enough_info decision - don't override with hasMissingInfo
        // The LLM already considers missing info when setting has_enough_info
        // Only use hasMissingInfo as a secondary signal when has_enough_info is false
        var hasEnoughInfoLoop1 = hasEnoughInfo;
        var hasEnoughInfoLoop2And3 = hasEnoughInfo;

        return currentLoop switch
        {
            1 => EvaluateFirstLoop(context, hasEnoughInfoLoop1),
            2 => EvaluateSecondLoop(context, hasEnoughInfoLoop2And3),
            3 => EvaluateThirdLoop(context, hasEnoughInfoLoop2And3),
            _ => new OrchestratorDecision
            {
                Action = "escalate",
                Reasoning = "Loop limit exceeded",
                ConfidenceScore = 0.3m,
                NextAgent = "human"
            }
        };
    }

    /// <summary>
    /// Loop 1: Triage + Research → Ask follow-up questions
    /// Goal: Gather initial context and ask clarifying questions
    /// </summary>
    private OrchestratorDecision EvaluateFirstLoop(RunContext context, bool hasEnoughInfo)
    {
        if (context.ExecutionState != null)
        {
            context.ExecutionState.LoopActionTaken = "ask_clarifying_questions";
        }

        if (hasEnoughInfo)
        {
            if (context.ExecutionState != null)
            {
                context.ExecutionState.LoopActionTaken = "provide_response";
            }

            return new OrchestratorDecision
            {
                Action = "respond",
                Reasoning = "First loop: Sufficient information already provided. Providing response.",
                ConfidenceScore = 0.8m,
                NextAgent = "response_agent"
            };
        }

        return new OrchestratorDecision
        {
            Action = "respond_with_questions",
            Reasoning = "First loop: Triage complete. Now asking user for clarification.",
            ConfidenceScore = 0.7m,
            NextAgent = "response_agent"
        };
    }

    /// <summary>
    /// Loop 2: User provides answers → Triage + Research with answers
    /// Decision: Provide response OR ask more questions
    /// </summary>
    private OrchestratorDecision EvaluateSecondLoop(RunContext context, bool hasEnoughInfo)
    {
        if (!hasEnoughInfo)
        {
            if (context.ExecutionState != null)
            {
                context.ExecutionState.LoopActionTaken = "ask_more_questions";
            }

            return new OrchestratorDecision
            {
                Action = "respond_with_questions",
                Reasoning = "Second loop: Still need more info. Asking additional clarifying questions.",
                ConfidenceScore = 0.6m,
                NextAgent = "response_agent"
            };
        }

        // Have enough info - provide response
        if (context.ExecutionState != null)
        {
            context.ExecutionState.LoopActionTaken = "provide_response";
        }

        return new OrchestratorDecision
        {
            Action = "respond",
            Reasoning = "Second loop: Sufficient information from user. Providing automated response.",
            ConfidenceScore = 0.8m,
            NextAgent = "response_agent"
        };
    }

    /// <summary>
    /// Loop 3: User provides more answers → Triage + Research
    /// Decision: Provide response OR ask final round of questions
    /// Escalation only happens if loop 4 is needed
    /// </summary>
    private OrchestratorDecision EvaluateThirdLoop(RunContext context, bool hasEnoughInfo)
    {
        if (!hasEnoughInfo)
        {
            // Still need more info - ask final round of questions
            // Bot will escalate in loop 4 if user still can't provide enough info
            if (context.ExecutionState != null)
            {
                context.ExecutionState.LoopActionTaken = "ask_final_questions";
            }

            return new OrchestratorDecision
            {
                Action = "respond_with_questions",
                Reasoning = "Third loop: Still need more info. Asking final clarifying questions before potential escalation.",
                ConfidenceScore = 0.5m,
                NextAgent = "response_agent"
            };
        }

        // Have enough info - provide final response with evidence
        if (context.ExecutionState != null)
        {
            context.ExecutionState.LoopActionTaken = "provide_final_response";
        }

        return new OrchestratorDecision
        {
            Action = "respond",
            Reasoning = "Third loop: Final response with key evidence and next steps.",
            ConfidenceScore = 0.85m,
            NextAgent = "response_agent"
        };
    }

    /// <summary>
    /// Assess if follow-ups are needed after response is generated
    /// </summary>
    public OrchestratorDecision DecideFollowUp(RunContext context, bool responseGenerated)
    {
        if (!responseGenerated)
        {
            return new OrchestratorDecision
            {
                Action = "escalate",
                Reasoning = "Response generation failed. Escalating.",
                ConfidenceScore = 0.3m,
                NextAgent = "human"
            };
        }

        var uncertaintyIndicators = new[]
        {
            context.ShouldAskFollowUps,
            (context.FollowUpQuestions?.Count ?? 0) > 0,
            false
        };

        var uncertaintyCount = uncertaintyIndicators.Count(x => x);

        return uncertaintyCount >= 2
            ? new OrchestratorDecision
            {
                Action = "follow_up",
                Reasoning = "Multiple uncertainty indicators suggest follow-up questions are needed.",
                ConfidenceScore = 0.75m,
                NextAgent = "response_agent"
            }
            : new OrchestratorDecision
            {
                Action = "finalize",
                Reasoning = "Response is complete and sufficient.",
                ConfidenceScore = 0.85m,
                NextAgent = "none"
            };
    }

    public async Task<ResearchDirective> DecideResearchAsync(
        RunContext context,
        TriageResult triageResult,
        CasePacket casePacket,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetResearchDirectiveSchema();

        var availableTools = string.Join(", ", new[]
        {
            "GitHubSearchTool",
            "DocumentationSearchTool",
            "CodeAnalysisTool",
            "ValidationTool",
            "WebSearchTool"
        });

        var categoriesText = string.Join(", ", triageResult.Categories);
        var extractedText = string.Join("; ", triageResult.ExtractedDetails.Select(kv => $"{kv.Key}: {kv.Value}"));
        var caseFields = string.Join("; ", casePacket.Fields.Select(kv => $"{kv.Key}: {kv.Value}"));

        try
        {
            var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
                "orchestrator-research-gate.md",
                new Dictionary<string, string>
                {
                    ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                    ["ISSUE_BODY"] = context.Issue.Body ?? string.Empty,
                    ["CATEGORIES"] = categoriesText,
                    ["TRIAGE_CONFIDENCE"] = triageResult.ConfidenceScore.ToString("0.00"),
                    ["EXTRACTED_DETAILS"] = extractedText,
                    ["CASE_PACKET_FIELDS"] = caseFields,
                    ["AVAILABLE_TOOLS"] = availableTools
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
                SchemaName = "ResearchDirective",
                Temperature = 0.2
            };

            var response = await _llmClient.CompleteAsync(request, cancellationToken);
            return ParseResearchDirective(response, schema);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): Failed to decide research directive: {ex.Message}");
            return new ResearchDirective
            {
                ShouldResearch = true,
                AllowedTools = new List<string> { "GitHubSearchTool", "DocumentationSearchTool" },
                ToolPriority = new List<string>(),
                AllowWebSearch = false,
                QueryQuality = "low",
                RecommendedQuery = string.Empty,
                MaxTools = 2,
                MaxFindings = 5,
                Reasoning = "Research gate failed; using safe default."
            };
        }
    }

    /// <summary>
    /// Replan if current approach isn't working
    /// </summary>
    public async Task<OrchestratorPlan> ReplanAsync(
        RunContext context,
        OrchestratorPlan currentPlan,
        List<string> failedSteps,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetPlanSchema();
        
        var failedStepsText = string.Join("\n- ", failedSteps);
        
        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "orchestrator-replan.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["PROBLEM_SUMMARY"] = currentPlan.ProblemSummary ?? string.Empty,
                ["FAILED_STEPS"] = $"- {failedStepsText}",
                ["INFO_NEEDED"] = $"- {string.Join("\n- ", currentPlan.InformationNeeded)}"
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
            SchemaName = "OrchestratorPlan",
            Temperature = 0.5
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        if (!response.IsSuccess)
        {
            return currentPlan; // Fall back to current plan
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);
            return new OrchestratorPlan
            {
                ProblemSummary = currentPlan.ProblemSummary, // Keep original understanding
                InformationNeeded = json.GetProperty("information_needed")
                    .EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList(),
                InvestigationSteps = json.GetProperty("investigation_steps")
                    .EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList(),
                LikelyResolution = json.GetProperty("likely_resolution").GetBoolean(),
                Reasoning = json.GetProperty("reasoning").GetString() ?? ""
            };
        }
        catch
        {
            return currentPlan;
        }
    }

    /// <summary>
    /// Detect if user has self-resolved by indicating they know the fix or will try something.
    /// This prevents asking unnecessary follow-up questions.
    /// </summary>
    private static bool DetectSelfResolution(RunContext context)
    {
        // Only check on loop 2+ (after user has responded to questions)
        var loopCount = context.ActiveUserConversation?.LoopCount ?? context.CurrentLoopCount ?? 1;
        if (loopCount < 2)
        {
            return false;
        }

        // Get the latest user comment - use IncomingComment if available (triggered by comment),
        // otherwise use Issue body (triggered by issue opened)
        var latestComment = context.IncomingComment?.Body ?? context.Issue?.Body;

        if (string.IsNullOrWhiteSpace(latestComment))
        {
            return false;
        }

        var lowerComment = latestComment.ToLowerInvariant();

        // Patterns indicating user knows the solution
        var selfResolutionPatterns = new[]
        {
            "i'll try",
            "i will try",
            "i'll add",
            "i will add",
            "i'll set",
            "i will set",
            "i'll update",
            "i will update",
            "i'll change",
            "i will change",
            "i'll fix",
            "i will fix",
            "i need to",
            "i should",
            "let me try",
            "going to try",
            "gonna try",
            "that worked",
            "this worked",
            "it works now",
            "solved it",
            "fixed it",
            "found the issue",
            "found the problem",
            "figured it out",
            "that was the issue",
            "that was the problem",
            "my mistake",
            "i forgot to",
            "i missed",
            "i didn't set",
            "i didn't have",
            "wasn't set",
            "wasn't configured",
            "missing the",
            "need to add"
        };

        return selfResolutionPatterns.Any(pattern => lowerComment.Contains(pattern));
    }

    /// <summary>
    /// Build conversation context including user's most recent comment/answers
    /// This helps the sufficiency check understand what information the user has already provided
    /// </summary>
    private static string BuildConversationContext(RunContext context)
    {
        var sb = new System.Text.StringBuilder();
        
        // Include the user's most recent comment (their answers to our questions)
        if (context.IncomingComment?.Body != null && !string.IsNullOrWhiteSpace(context.IncomingComment.Body))
        {
            var author = context.IncomingComment.User?.Login ?? "User";
            sb.AppendLine("--- Latest Comment from User ---");
            sb.AppendLine($"From: {author}");
            sb.AppendLine(context.IncomingComment.Body);
            sb.AppendLine();
        }
        
        // Include what the bot previously asked (so we know what questions were answered)
        var currentLoop = context.CurrentLoopCount ?? 1;
        if (currentLoop > 1)
        {
            sb.AppendLine($"--- Context ---");
            sb.AppendLine($"This is loop {currentLoop}. The bot has already asked questions in previous loops.");
            sb.AppendLine("The user's comment above is their response to those questions.");
            sb.AppendLine();
        }
        
        return sb.ToString().Trim();
    }

    private async Task<InfoSufficiencyResult> AssessInformationSufficiencyAsync(
        RunContext context,
        OrchestratorPlan plan,
        CancellationToken cancellationToken)
    {
        var schema = OrchestrationSchemas.GetInfoSufficiencySchema();
        var fieldsText = string.Join("\n", context.CasePacket.Fields.Select(kv => $"- {kv.Key}: {kv.Value}"));
        var missingFields = context.Scoring?.MissingFields?.Count > 0
            ? string.Join(", ", context.Scoring.MissingFields)
            : "none";

        // Build conversation context including user's comment/answers
        var conversationContext = BuildConversationContext(context);

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "orchestrator-sufficiency.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["ISSUE_BODY"] = context.Issue.Body ?? string.Empty,
                ["CATEGORY"] = context.CategoryDecision?.Category ?? "unknown",
                ["CASE_PACKET_FIELDS"] = string.IsNullOrWhiteSpace(fieldsText) ? "none" : fieldsText,
                ["MISSING_FIELDS"] = missingFields,
                ["PLAN_INFO_NEEDED"] = plan?.InformationNeeded?.Count > 0 ? string.Join(", ", plan.InformationNeeded) : "none",
                ["CONVERSATION_HISTORY"] = conversationContext
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
            SchemaName = "InfoSufficiency",
            Temperature = 0.2
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        if (!response.IsSuccess)
        {
            return new InfoSufficiencyResult(
                false,
                new List<string> { "sufficiency_check_failed" },
                "LLM sufficiency check failed");
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);
            var hasEnoughInfo = json.GetProperty("has_enough_info").GetBoolean();
            var missing = json.TryGetProperty("missing_info", out var missingProp)
                ? missingProp.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                : new List<string>();
            var reasoning = json.GetProperty("reasoning").GetString() ?? string.Empty;

            return new InfoSufficiencyResult(hasEnoughInfo, missing, reasoning);
        }
        catch
        {
            return new InfoSufficiencyResult(
                false,
                new List<string> { "sufficiency_parse_failed" },
                "Failed to parse sufficiency response");
        }
    }

    private sealed record InfoSufficiencyResult(bool HasEnoughInfo, List<string> MissingInfo, string Reasoning);

    private ResearchDirective ParseResearchDirective(LlmResponse response, string schema)
    {
        var defaultDirective = new ResearchDirective
        {
            ShouldResearch = true,
            AllowedTools = new List<string> { "GitHubSearchTool", "DocumentationSearchTool" },
            ToolPriority = new List<string>(),
            AllowWebSearch = false,
            QueryQuality = "low",
            RecommendedQuery = string.Empty,
            MaxTools = 2,
            MaxFindings = 5,
            Reasoning = "Default research directive."
        };

        if (!response.IsSuccess)
        {
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): LLM response was not successful. RawResponse: {Truncate(response.RawResponse, 500)}");
            defaultDirective.Reasoning = $"LLM call failed: IsSuccess=false";
            return defaultDirective;
        }

        // Log schema validation but don't fail on it - try to extract values anyway
        if (!_schemaValidator.TryValidate(response.Content, schema, out var validationErrors))
        {
            var errorDetails = validationErrors != null && validationErrors.Count > 0 
                ? string.Join("; ", validationErrors) 
                : "Unknown validation error";
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): Schema validation warning: {errorDetails}");
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): Will attempt to extract values from response anyway...");
        }

        // Try to parse JSON and extract values (even if schema validation failed)
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);
            
            // Extract tools with fallback to defaults
            var tools = json.TryGetProperty("allowed_tools", out var toolsProp)
                ? toolsProp.EnumerateArray()
                    .Select(t => t.GetString() ?? string.Empty)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList()
                : new List<string>();
            
            // If no allowed_tools but tool_priority exists, use that as allowed_tools
            var priority = json.TryGetProperty("tool_priority", out var priorityProp)
                ? priorityProp.EnumerateArray()
                    .Select(t => t.GetString() ?? string.Empty)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList()
                : new List<string>();
            
            if (tools.Count == 0 && priority.Count > 0)
            {
                tools = new List<string>(priority);
            }
            
            // Default allowed_tools if still empty
            if (tools.Count == 0)
            {
                tools = new List<string> { "GitHubSearchTool", "DocumentationSearchTool" };
            }

            // Extract query quality - this is the critical field for WebSearchTool
            var queryQuality = json.TryGetProperty("query_quality", out var quality) 
                ? quality.GetString() ?? "low" 
                : "low";
            
            // Check if web search should be allowed
            // Allow web search if: explicitly allowed OR (query_quality is high AND WebSearchTool is in tools/priority)
            var allowWebSearch = json.TryGetProperty("allow_web_search", out var allowWeb) && allowWeb.GetBoolean();
            
            // If LLM said query_quality is "high" but forgot to set allow_web_search, infer it
            if (!allowWebSearch && queryQuality == "high")
            {
                var hasWebSearchInTools = tools.Any(t => t.Contains("WebSearch", StringComparison.OrdinalIgnoreCase)) ||
                                          priority.Any(t => t.Contains("WebSearch", StringComparison.OrdinalIgnoreCase));
                if (hasWebSearchInTools)
                {
                    Console.WriteLine($"[MAF] Orchestrator(ResearchGate): Inferring allow_web_search=true from query_quality=high and WebSearchTool in tools");
                    allowWebSearch = true;
                }
            }
            
            // Add WebSearchTool to allowed tools if allow_web_search is true and not already present
            if (allowWebSearch && !tools.Any(t => t.Contains("WebSearch", StringComparison.OrdinalIgnoreCase)))
            {
                tools.Add("WebSearchTool");
            }

            var directive = new ResearchDirective
            {
                ShouldResearch = json.TryGetProperty("should_research", out var shouldResearch) && shouldResearch.GetBoolean(),
                AllowedTools = tools,
                ToolPriority = priority,
                AllowWebSearch = allowWebSearch,
                QueryQuality = queryQuality,
                RecommendedQuery = json.TryGetProperty("recommended_query", out var query) ? query.GetString() ?? string.Empty : string.Empty,
                MaxTools = json.TryGetProperty("max_tools", out var maxTools) ? maxTools.GetInt32() : 3,
                MaxFindings = json.TryGetProperty("max_findings", out var maxFindings) ? maxFindings.GetInt32() : 5,
                Reasoning = json.TryGetProperty("reasoning", out var reasoning) ? reasoning.GetString() ?? "Extracted from LLM response" : "Extracted from LLM response"
            };
            
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): Successfully parsed directive - query_quality={directive.QueryQuality}, allow_web_search={directive.AllowWebSearch}");
            return directive;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): JSON parsing failed: {ex.Message}");
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): Response content: {Truncate(response.Content, 500)}");
            defaultDirective.Reasoning = $"JSON parsing failed: {ex.Message}";
            return defaultDirective;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength) + "…";
    }
}
/// Orchestrator's initial plan for handling an issue
/// </summary>
public class OrchestratorPlan
{
    public string ProblemSummary { get; set; } = "";
    public List<string> InformationNeeded { get; set; } = new();
    public List<string> InvestigationSteps { get; set; } = new();
    public bool LikelyResolution { get; set; }
    public string Reasoning { get; set; } = "";
}

/// <summary>
/// Orchestrator's decision for next action
/// </summary>
public class OrchestratorDecision
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = ""; // research, respond, follow_up, finalize, escalate

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";

    [JsonPropertyName("confidence_score")]
    public decimal ConfidenceScore { get; set; } // 0-1

    [JsonPropertyName("next_agent")]
    public string NextAgent { get; set; } = ""; // research_agent, response_agent, human, none
}

