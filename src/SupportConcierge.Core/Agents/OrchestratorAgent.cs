using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Schemas;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Agents;

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
    private const int MaxLoops = 3;

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
        
        var prompt = $@"You are the orchestrator for a GitHub issue support bot. Analyze this issue and create a structured plan.

Issue Title: {context.Issue.Title}
Issue Body: {context.Issue.Body}

Your task is to:
1. Understand the core problem
2. Identify what information is needed
3. Plan investigation steps
4. Determine if you can likely resolve it within 3 loops

Output a JSON plan with:
- problem_summary: Brief understanding of the issue
- information_needed: Key details to investigate
- investigation_steps: Ordered list of what to investigate
- likely_resolution: Can this be resolved? (true/false)
- reasoning: Why/why not can it be resolved";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are an expert issue triage orchestrator. Analyze issues and create actionable investigation plans." },
                new() { Role = "user", Content = prompt }
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
    /// </summary>
    public async Task<OrchestratorDecision> EvaluateProgressAsync(
        RunContext context,
        OrchestratorPlan plan,
        int currentLoop,
        List<OrchestratorDecision> previousDecisions,
        CancellationToken cancellationToken = default)
    {
        if (currentLoop >= MaxLoops)
        {
            return new OrchestratorDecision
            {
                Action = "escalate",
                Reasoning = $"Maximum loops ({MaxLoops}) reached. Escalating to human review.",
                ConfidenceScore = 0.5m,
                NextAgent = "human"
            };
        }

        // Check if resolution is achievable with current information
        var hasEnoughInfo = context.CategoryDecision != null &&
                            context.CasePacket.Fields.Count > 0;

        if (!hasEnoughInfo && currentLoop < MaxLoops - 1)
        {
            return new OrchestratorDecision
            {
                Action = "research",
                Reasoning = "Need more information. Continuing research.",
                ConfidenceScore = 0.7m,
                NextAgent = "research_agent"
            };
        }

        // Ready to generate response
        if (hasEnoughInfo || currentLoop == MaxLoops - 1)
        {
            return new OrchestratorDecision
            {
                Action = "respond",
                Reasoning = "Sufficient information gathered. Time to respond.",
                ConfidenceScore = 0.8m,
                NextAgent = "response_agent"
            };
        }

        return new OrchestratorDecision
        {
            Action = "research",
            Reasoning = "Continue information gathering.",
            ConfidenceScore = 0.6m,
            NextAgent = "research_agent"
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
        
        var prompt = $@"You are the orchestrator for a GitHub issue support bot. Previous investigation steps failed.

Issue Title: {context.Issue.Title}
Current Problem Summary: {currentPlan.ProblemSummary}

Failed Investigation Steps:
- {failedStepsText}

Previous Information Needed:
- {string.Join("\n- ", currentPlan.InformationNeeded)}

Replan the investigation with different approaches. Avoid the failed steps.
Output updated plan with new investigation_steps, modified information_needed, and reasoning for the changes.";

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = "You are an expert issue triage orchestrator. Adapt investigation plans when initial approaches fail." },
                new() { Role = "user", Content = prompt }
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
}

/// <summary>
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
