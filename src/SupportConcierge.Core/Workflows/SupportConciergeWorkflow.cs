using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Tools;
using SupportConcierge.Core.Workflows.Executors;

namespace SupportConcierge.Core.Workflows;

/// <summary>
/// MAF Workflow for GitHub issue support bot
/// 
/// DAG Flow:
/// ParseEvent → LoadState → Guardrails → [Decision: /stop or finalized?]
///   ↓ No → Triage → Research → Response → OrchestratorEvaluate 
///   ↓ [Decision: ask questions/finalize/escalate?]
///   ├→ AskFollowUps → PostComment → PersistState → Exit
///   ├→ Finalize → PostComment → PersistState → Exit
///   ├→ Escalate → PostComment → PersistState → Exit
///   ├→ Continue Loop → Triage (max 3 iterations)
///   ↓ Yes → PostComment → PersistState → Exit
/// 
/// Key Improvements:
/// - State loading from previous comments for persistence
/// - Author gating (only issue author can interact)
/// - Loop limit enforcement (max 3 iterations)
/// - Proper command parsing (/stop, /diagnose)
/// - State embedding in comments
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
        CasePacketAgent casePacketAgent,
        OffTopicAgent offTopicAgent,
        ToolRegistry toolRegistry,
        ISpecPackLoader specPackLoader,
        IGitHubTool gitHubTool)
    {
        // Create executors
        var parseEvent = new ParseEventExecutor();
        var loadState = new LoadStateExecutor(gitHubTool);
        var guardrails = new GuardrailsExecutor();
        var offTopic = new OffTopicCheckExecutor(offTopicAgent);
        var loadSpecPack = new LoadSpecPackExecutor(specPackLoader);
        var triage = new TriageExecutor(triageAgent, criticAgent);
        var casePacket = new CasePacketExecutor(gitHubTool, casePacketAgent);
        var researchGate = new OrchestratorResearchGateExecutor(orchestratorAgent);
        var research = new ResearchExecutor(researchAgent, criticAgent, toolRegistry);
        var response = new ResponseExecutor(responseAgent, criticAgent);
        var orchestratorEvaluate = new OrchestratorEvaluateExecutor(orchestratorAgent);
        var postComment = new PostCommentExecutor(gitHubTool);
        var persistState = new PersistStateExecutor();

        // Build workflow DAG
        var builder = new WorkflowBuilder(parseEvent)
            .BindExecutor(loadState)
            .BindExecutor(guardrails)
            .BindExecutor(offTopic)
            .BindExecutor(loadSpecPack)
            .BindExecutor(triage)
            .BindExecutor(casePacket)
            .BindExecutor(researchGate)
            .BindExecutor(research)
            .BindExecutor(response)
            .BindExecutor(orchestratorEvaluate)
            .BindExecutor(postComment)
            .BindExecutor(persistState);

        // Main flow: ParseEvent → LoadState → Guardrails
        builder.AddEdge(parseEvent, loadState);
        builder.AddEdge(loadState, guardrails);

        // After guardrails: check for /stop or finalized
        builder.AddEdge<RunContext>(guardrails, postComment, ctx => ctx?.ShouldStop ?? false);

        // Normal flow: Guardrails → Triage → Research → Response → Evaluate
        builder.AddEdge<RunContext>(guardrails, offTopic, ctx => !(ctx?.ShouldStop ?? false));
        builder.AddEdge<RunContext>(offTopic, postComment, ctx => ctx?.ShouldRedirectOffTopic ?? false);
        builder.AddEdge<RunContext>(offTopic, loadSpecPack, ctx => !(ctx?.ShouldRedirectOffTopic ?? false));
        builder.AddEdge(loadSpecPack, triage);
        builder.AddEdge(triage, casePacket);
        builder.AddEdge(casePacket, researchGate);
        builder.AddEdge(researchGate, research);
        builder.AddEdge(research, response);
        builder.AddEdge(response, orchestratorEvaluate);

        // After orchestrator evaluation: decide next action
        // CRITICAL: These edges must be mutually exclusive to prevent duplicate comments
        // The OrchestratorEvaluateExecutor ensures only ONE flag is set at a time
        
        // Decision 1: Ask follow-up questions (highest priority, most specific)
        builder.AddEdge<RunContext>(orchestratorEvaluate, postComment, ctx =>
            (ctx?.ShouldAskFollowUps ?? false));

        // Decision 2: Finalize (actionable) - only if NOT asking follow-ups
        builder.AddEdge<RunContext>(orchestratorEvaluate, postComment, ctx =>
            (ctx?.ShouldFinalize ?? false) && !(ctx?.ShouldAskFollowUps ?? false));

        // Decision 3: Escalate - only if NOT finalizing or asking follow-ups
        builder.AddEdge<RunContext>(orchestratorEvaluate, postComment, ctx =>
            (ctx?.ShouldEscalate ?? false) && !(ctx?.ShouldFinalize ?? false) && !(ctx?.ShouldAskFollowUps ?? false));

        // After posting, persist state
        builder.AddEdge(postComment, persistState);

        // Decision 4: Loop back for another iteration (if we haven't hit the limit)
        builder.AddEdge<RunContext>(orchestratorEvaluate, triage, ctx =>
            !((ctx?.ShouldFinalize ?? false) || (ctx?.ShouldEscalate ?? false) || (ctx?.ShouldAskFollowUps ?? false)) && 
            ((ctx?.CurrentLoopCount ?? 0) < 3));

        return builder.Build();
    }
}
