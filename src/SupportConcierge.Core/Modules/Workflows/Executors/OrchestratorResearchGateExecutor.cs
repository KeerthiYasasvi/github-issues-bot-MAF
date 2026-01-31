using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Modules.Agents;
using SupportConcierge.Core.Modules.Models;

namespace SupportConcierge.Core.Modules.Workflows.Executors;

/// <summary>
/// MAF Executor: Orchestrator research gating - decides if/which tools to use.
/// </summary>
public sealed class OrchestratorResearchGateExecutor : Executor<RunContext, RunContext>
{
    private readonly OrchestratorAgent _orchestrator;

    public OrchestratorResearchGateExecutor(OrchestratorAgent orchestrator)
        : base("orchestrator_research_gate", ExecutorDefaults.Options, false)
    {
        _orchestrator = orchestrator;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        try
        {
            var triageResult = input.TriageResult ?? new TriageResult();
            var casePacket = input.CasePacket ?? new CasePacket();

            var directive = await _orchestrator.DecideResearchAsync(input, triageResult, casePacket, ct);
            input.ResearchDirective = directive;

            var tools = directive.AllowedTools.Count > 0
                ? string.Join(", ", directive.AllowedTools)
                : "none";

            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): should_research={directive.ShouldResearch}, allow_web_search={directive.AllowWebSearch}, query_quality={directive.QueryQuality}");
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): allowed_tools={tools}");
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): budget=max_tools={directive.MaxTools}, max_findings={directive.MaxFindings}");
            if (directive.ToolPriority.Count > 0)
            {
                Console.WriteLine($"[MAF] Orchestrator(ResearchGate): tool_priority={string.Join(", ", directive.ToolPriority)}");
            }
            if (!string.IsNullOrWhiteSpace(directive.RecommendedQuery))
            {
                Console.WriteLine($"[MAF] Orchestrator(ResearchGate): recommended_query={directive.RecommendedQuery}");
            }
            if (!string.IsNullOrWhiteSpace(directive.Reasoning))
            {
                Console.WriteLine($"[MAF] Orchestrator(ResearchGate): reasoning={directive.Reasoning}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] Orchestrator(ResearchGate): Exception while deciding research directive: {ex.Message}");
            input.ResearchDirective = new ResearchDirective
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

        return input;
    }
}

