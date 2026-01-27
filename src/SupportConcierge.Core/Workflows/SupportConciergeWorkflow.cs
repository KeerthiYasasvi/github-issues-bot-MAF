using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Tools;
using SupportConcierge.Core.Workflows.Executors;

namespace SupportConcierge.Core.Workflows;

/// <summary>
/// MAF Workflow for GitHub issue support bot
/// DAG: ParseEvent → Guardrails → [Decision] → TriageExecutor → ResearchExecutor → ResponseExecutor → OrchestratorEvaluate → [Decision] → PersistState
/// </summary>
public static class SupportConciergeWorkflow
{
    /// <summary>
    /// Build the MAF workflow with all executors and conditional edges
    /// </summary>
    public static Workflow Build(
        EnhancedTriageAgent triageAgent,
        EnhancedResearchAgent researchAgent,
        EnhancedResponseAgent responseAgent,
        CriticAgent criticAgent,
        OrchestratorAgent orchestratorAgent,
        ToolRegistry toolRegistry,
        IGitHubTool gitHubTool)
    {
        // Create executors
        var parseEvent = new ParseEventExecutor();
        var guardrails = new GuardrailsExecutor();
        var triage = new TriageExecutor(triageAgent, criticAgent);
        var research = new ResearchExecutor(researchAgent, criticAgent, toolRegistry);
        var response = new ResponseExecutor(responseAgent, criticAgent);
        var orchestratorEvaluate = new OrchestratorEvaluateExecutor(orchestratorAgent);
        var postComment = new PostCommentExecutor(gitHubTool);
        var persistState = new PersistStateExecutor();

        // Build workflow DAG
        var builder = new WorkflowBuilder(parseEvent)
            .BindExecutor(guardrails)
            .BindExecutor(triage)
            .BindExecutor(research)
            .BindExecutor(response)
            .BindExecutor(orchestratorEvaluate)
            .BindExecutor(postComment)
            .BindExecutor(persistState);

        // Linear edges: parseEvent → guardrails → triage → research → response → orchestratorEvaluate → persistState
        builder.AddEdge(parseEvent, guardrails);

        // After guardrails, check for /stop
        builder.AddEdge<RunContext>(guardrails, persistState, ctx => ctx?.ShouldStop ?? false);
        builder.AddEdge<RunContext>(guardrails, triage, ctx => !(ctx?.ShouldStop ?? false));

        // After triage, continue to research
        builder.AddEdge(triage, research);

        // After research, continue to response
        builder.AddEdge(research, response);

        // After response, evaluate with orchestrator
        builder.AddEdge(response, orchestratorEvaluate);

        // After orchestrator evaluates, decide next action
        // If finalize or escalate or follow_up, post comment then persist and exit
        builder.AddEdge<RunContext>(orchestratorEvaluate, postComment, ctx =>
            (ctx?.ShouldFinalize ?? false) || (ctx?.ShouldEscalate ?? false) || (ctx?.ShouldAskFollowUps ?? false) || ((ctx?.CurrentLoopCount ?? 0) >= 3));
        builder.AddEdge(postComment, persistState);

        // If loop < 3 and no terminal decision, loop back to triage
        builder.AddEdge<RunContext>(orchestratorEvaluate, triage, ctx => 
            !((ctx?.ShouldFinalize ?? false) || (ctx?.ShouldEscalate ?? false) || (ctx?.ShouldAskFollowUps ?? false)) && ((ctx?.CurrentLoopCount ?? 0) < 3));

        return builder.Build();
    }
}
