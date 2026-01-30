using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Research - select tools, execute, investigate, critique and deep dive if needed
/// </summary>
public sealed class ResearchExecutor : Executor<RunContext, RunContext>
{
    private readonly EnhancedResearchAgent _researchAgent;
    private readonly CriticAgent _criticAgent;
    private readonly ToolRegistry _toolRegistry;

    public ResearchExecutor(EnhancedResearchAgent researchAgent, CriticAgent criticAgent, ToolRegistry toolRegistry)
        : base("research", ExecutorDefaults.Options, false)
    {
        _researchAgent = researchAgent;
        _criticAgent = criticAgent;
        _toolRegistry = toolRegistry;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        Console.WriteLine("[MAF] Research: Starting tool selection and investigation");

        var triageResult = input.TriageResult ?? new TriageResult();

        // Select tools
        var selectedTools = await _researchAgent.SelectToolsAsync(input, triageResult, ct);
        input.SelectedTools = selectedTools.SelectedTools.ToList();
        Console.WriteLine($"[MAF] Research: Selected {selectedTools.SelectedTools.Count} tools");
        if (selectedTools.SelectedTools.Count > 0)
        {
            var toolPreview = string.Join("; ", selectedTools.SelectedTools.Take(3)
                .Select(t =>
                {
                    var paramKeys = t.QueryParameters?.Keys?.Take(4) ?? Enumerable.Empty<string>();
                    var paramPreview = paramKeys.Any() ? $" params[{string.Join(",", paramKeys)}]" : "";
                    return $"{t.ToolName} ({Truncate(t.Reasoning, 80)}){paramPreview}";
                }));
            Console.WriteLine($"[MAF] Research: Tools = {toolPreview}");
        }

        // Execute tools
        var toolResults = new Dictionary<string, string>();
        foreach (var selectedTool in selectedTools.SelectedTools)
        {
            var parameters = new Dictionary<string, string>(selectedTool.QueryParameters, StringComparer.OrdinalIgnoreCase);
            var owner = input.Repository?.Owner?.Login ?? string.Empty;
            var repo = input.Repository?.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(owner) && !parameters.ContainsKey("owner"))
            {
                parameters["owner"] = owner;
            }
            if (!string.IsNullOrWhiteSpace(repo) && !parameters.ContainsKey("repo"))
            {
                parameters["repo"] = repo;
            }

            var result = await _toolRegistry.ExecuteAsync(selectedTool.ToolName, parameters, ct);
            if (result.Success)
            {
                toolResults[selectedTool.ToolName] = result.Content;
            }
            else
            {
                toolResults[selectedTool.ToolName] = $"Error: {result.Error}";
            }
        }

        // Investigate
        var investigationResult = await _researchAgent.InvestigateAsync(input, triageResult, selectedTools, toolResults, ct);
        Console.WriteLine($"[MAF] Research: Found {investigationResult.Findings.Count} findings");
        LogFindings("Research", investigationResult);

        // Critique research
        var researchCritique = await _criticAgent.CritiqueResearchAsync(input, null, investigationResult.Findings.Select(f => f.Content).ToList(), ct);
        if (!researchCritique.IsPassable)
        {
            Console.WriteLine($"[MAF] Research (Critique): Failed critique (score: {researchCritique.Score}/10), performing deep dive...");
            LogCritiqueSummary("Research", researchCritique);
            var deepDiveResults = await _researchAgent.DeepDiveAsync(input, triageResult, investigationResult, researchCritique, new Dictionary<string, string>(), ct);
            investigationResult = deepDiveResults;
            input.ResearchDeepDived = true;
            Console.WriteLine($"[MAF] Research: Deep dive completed, now {investigationResult.Findings.Count} findings");
            LogFindings("Research", investigationResult);
        }
        else
        {
            Console.WriteLine($"[MAF] Research (Critique): Passed critique (score: {researchCritique.Score}/10)");
        }

        input.InvestigationResult = investigationResult;
        return input;
    }

    private static void LogFindings(string stage, InvestigationResult result)
    {
        var topFindings = result.Findings.Take(3)
            .Select(f => $"{f.FindingType} (conf {f.Confidence:0.00}) {Truncate(f.Content, 120)}")
            .ToList();
        if (topFindings.Count > 0)
        {
            Console.WriteLine($"[MAF] {stage}: Findings = {string.Join(" | ", topFindings)}");
        }
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
