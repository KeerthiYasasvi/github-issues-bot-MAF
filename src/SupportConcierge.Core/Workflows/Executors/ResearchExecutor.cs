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
        Console.WriteLine($"[MAF] Research: Selected {selectedTools.SelectedTools.Count} tools");

        // Execute tools
        var toolResults = new Dictionary<string, string>();
        foreach (var selectedTool in selectedTools.SelectedTools)
        {
            var result = await _toolRegistry.ExecuteAsync(selectedTool.ToolName, selectedTool.QueryParameters, ct);
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

        // Critique research
        var researchCritique = await _criticAgent.CritiqueResearchAsync(input, null, investigationResult.Findings.Select(f => f.Content).ToList(), ct);
        if (!researchCritique.IsPassable)
        {
            Console.WriteLine($"[MAF] Research: Failed critique (score: {researchCritique.Score}/10), performing deep dive...");
            var deepDiveResults = await _researchAgent.DeepDiveAsync(input, triageResult, investigationResult, researchCritique, new Dictionary<string, string>(), ct);
            investigationResult = deepDiveResults;
            Console.WriteLine($"[MAF] Research: Deep dive completed, now {investigationResult.Findings.Count} findings");
        }
        else
        {
            Console.WriteLine($"[MAF] Research: Passed critique (score: {researchCritique.Score}/10)");
        }

        input.InvestigationResult = investigationResult;
        return input;
    }
}
