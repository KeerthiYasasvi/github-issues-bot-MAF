using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Models;
using SupportConcierge.Workflows.Executors;

namespace SupportConcierge.Workflows;

public static class SupportConciergeWorkflow
{
    public static Workflow Build(WorkflowServices services)
    {
        var parse = new ParseEventExecutor(services);
        var loadSpec = new LoadSpecPackExecutor(services);
        var loadState = new LoadPriorStateExecutor(services);
        var guardrails = new ApplyGuardrailsExecutor(services);
        var extract = new ExtractCasePacketExecutor(services);
        var score = new ScoreCompletenessExecutor(services);
        var ackStop = new AcknowledgeStopExecutor(services);
        var genFollowups = new GenerateFollowUpsExecutor(services);
        var validateFollowups = new ValidateFollowUpsExecutor(services);
        var postFollowups = new PostFollowUpCommentExecutor(services);
        var searchDupes = new SearchDuplicatesExecutor(services);
        var fetchDocs = new FetchGroundingDocsExecutor(services);
        var genBrief = new GenerateEngineerBriefExecutor(services);
        var validateBrief = new ValidateBriefExecutor(services);
        var postBrief = new PostFinalBriefCommentExecutor(services);
        var applyRouting = new ApplyRoutingExecutor(services);
        var escalate = new EscalateExecutor(services);
        var persist = new PersistStateExecutor(services);

        var builder = new WorkflowBuilder(parse)
            .WithName("SupportConcierge")
            .WithDescription("GitHub issues support bot workflow")
            .BindExecutor(loadSpec)
            .BindExecutor(loadState)
            .BindExecutor(guardrails)
            .BindExecutor(extract)
            .BindExecutor(score)
            .BindExecutor(ackStop)
            .BindExecutor(genFollowups)
            .BindExecutor(validateFollowups)
            .BindExecutor(postFollowups)
            .BindExecutor(searchDupes)
            .BindExecutor(fetchDocs)
            .BindExecutor(genBrief)
            .BindExecutor(validateBrief)
            .BindExecutor(postBrief)
            .BindExecutor(applyRouting)
            .BindExecutor(escalate)
            .BindExecutor(persist)
            .WithOutputFrom(persist);

        builder.AddEdge(parse, loadSpec);
        builder.AddEdge(loadSpec, loadState);
        builder.AddEdge(loadState, guardrails);
        builder.AddEdge<RunContext>(guardrails, ackStop, ctx => ctx != null && ctx.ShouldStop);
        builder.AddEdge<RunContext>(guardrails, extract, ctx => ctx != null && !ctx.ShouldStop);
        builder.AddEdge(extract, score);
        builder.AddEdge<RunContext>(score, escalate, ctx => ctx != null && ctx.ShouldEscalate);
        builder.AddEdge<RunContext>(score, genFollowups, ctx => ctx != null && ctx.ShouldAskFollowUps);
        builder.AddEdge<RunContext>(score, postBrief, ShouldFinalizeOffTopic);
        builder.AddEdge<RunContext>(score, searchDupes, ctx => ctx != null && ctx.ShouldFinalize && !ShouldFinalizeOffTopic(ctx));
        builder.AddEdge(genFollowups, validateFollowups);
        builder.AddEdge(validateFollowups, postFollowups);
        builder.AddEdge(postFollowups, persist);
        builder.AddEdge(searchDupes, fetchDocs);
        builder.AddEdge(fetchDocs, genBrief);
        builder.AddEdge(genBrief, validateBrief);
        builder.AddEdge(validateBrief, postBrief);
        builder.AddEdge<RunContext>(postBrief, applyRouting, ctx => ctx != null && !ShouldFinalizeOffTopic(ctx));
        builder.AddEdge<RunContext>(postBrief, persist, ShouldFinalizeOffTopic);
        builder.AddEdge(applyRouting, persist);
        builder.AddEdge(ackStop, persist);
        builder.AddEdge(escalate, persist);

        return builder.Build();
    }

    private static bool ShouldFinalizeOffTopic(RunContext? context)
    {
        if (context == null)
        {
            return false;
        }

        var category = context.State?.Category ?? context.CategoryDecision?.Category;
        return string.Equals(category, "off_topic", StringComparison.OrdinalIgnoreCase);
    }
}
