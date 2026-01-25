using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Triage - classify issue, extract data, critique and refine
/// </summary>
public sealed class TriageExecutor : Executor<RunContext, RunContext>
{
    private readonly EnhancedTriageAgent _triageAgent;
    private readonly CriticAgent _criticAgent;

    public TriageExecutor(EnhancedTriageAgent triageAgent, CriticAgent criticAgent)
        : base("triage", ExecutorDefaults.Options, false)
    {
        _triageAgent = triageAgent;
        _criticAgent = criticAgent;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        Console.WriteLine("[MAF] Triage: Starting classification and extraction");

        // Classify and extract
        var triageResult = await _triageAgent.ClassifyAndExtractAsync(input, ct);
        input.CategoryDecision = new CategoryDecision
        {
            Category = triageResult.Categories.FirstOrDefault() ?? "unclassified",
            Confidence = (double)triageResult.ConfidenceScore,
            Reasoning = triageResult.Reasoning
        };
        Console.WriteLine($"[MAF] Triage: Category = {input.CategoryDecision.Category} (confidence: {triageResult.ConfidenceScore:F2})");

        // Critique triage
        var triageCritique = await _criticAgent.CritiqueTriageAsync(input, input.CategoryDecision, ct);
        if (!triageCritique.IsPassable)
        {
            Console.WriteLine($"[MAF] Triage: Failed critique (score: {triageCritique.Score}/10), refining...");
            triageResult = await _triageAgent.RefineAsync(input, triageResult, triageCritique, ct);
            input.CategoryDecision.Category = triageResult.Categories.FirstOrDefault() ?? "unclassified";
            Console.WriteLine($"[MAF] Triage: Refined category = {input.CategoryDecision.Category}");
        }
        else
        {
            Console.WriteLine($"[MAF] Triage: Passed critique (score: {triageCritique.Score}/10)");
        }

        input.TriageResult = triageResult;
        return input;
    }
}
