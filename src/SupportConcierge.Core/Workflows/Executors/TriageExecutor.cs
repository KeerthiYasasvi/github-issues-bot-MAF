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
        Console.WriteLine($"[MAF] Triage: Reasoning = {Truncate(triageResult.Reasoning, 240)}");
        if (triageResult.ExtractedDetails.Count > 0)
        {
            var detailsPreview = string.Join("; ", triageResult.ExtractedDetails.Take(3).Select(kv => $"{kv.Key}={Truncate(kv.Value, 80)}"));
            Console.WriteLine($"[MAF] Triage: Extracted details preview = {detailsPreview}");
        }

        // Critique triage
        var triageCritique = await _criticAgent.CritiqueTriageAsync(input, input.CategoryDecision, ct);
        if (!triageCritique.IsPassable)
        {
            Console.WriteLine($"[MAF] Triage (Critique): Failed critique (score: {triageCritique.Score}/10), refining...");
            LogCritiqueSummary("Triage", triageCritique);
            triageResult = await _triageAgent.RefineAsync(input, triageResult, triageCritique, ct);
            input.CategoryDecision.Category = triageResult.Categories.FirstOrDefault() ?? "unclassified";
            Console.WriteLine($"[MAF] Triage: Refined category = {input.CategoryDecision.Category}");
            Console.WriteLine($"[MAF] Triage: Refined reasoning = {Truncate(triageResult.Reasoning, 240)}");
        }
        else
        {
            Console.WriteLine($"[MAF] Triage (Critique): Passed critique (score: {triageCritique.Score}/10)");
        }

        input.TriageResult = triageResult;
        return input;
    }

    private static void LogCritiqueSummary(string stage, CritiqueResult critique)
    {
        var issues = critique.Issues
            .Take(2)
            .Select(i => $"[{i.Severity}/5] {i.Category}: {Truncate(i.Problem, 120)}")
            .ToList();
        var suggestions = critique.Suggestions.Take(2).Select(s => Truncate(s, 120)).ToList();

        if (issues.Count > 0)
        {
            Console.WriteLine($"[MAF] {stage} (Critique): Issues = {string.Join(" | ", issues)}");
        }
        if (suggestions.Count > 0)
        {
            Console.WriteLine($"[MAF] {stage} (Critique): Suggestions = {string.Join(" | ", suggestions)}");
        }
        if (!string.IsNullOrWhiteSpace(critique.Reasoning))
        {
            Console.WriteLine($"[MAF] {stage} (Critique): Reasoning = {Truncate(critique.Reasoning, 200)}");
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
