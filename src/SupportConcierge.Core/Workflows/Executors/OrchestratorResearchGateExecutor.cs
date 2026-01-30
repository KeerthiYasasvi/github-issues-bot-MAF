using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

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

            Console.WriteLine($"[MAF] ResearchGate: should_research={directive.ShouldResearch}, allow_web_search={directive.AllowWebSearch}, query_quality={directive.QueryQuality}");
            Console.WriteLine($"[MAF] ResearchGate: allowed_tools={tools}");
            if (!string.IsNullOrWhiteSpace(directive.RecommendedQuery))
            {
                Console.WriteLine($"[MAF] ResearchGate: recommended_query={directive.RecommendedQuery}");
            }
            if (!string.IsNullOrWhiteSpace(directive.Reasoning))
            {
                Console.WriteLine($"[MAF] ResearchGate: reasoning={directive.Reasoning}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] ResearchGate: Exception while deciding research directive: {ex.Message}");
            input.ResearchDirective = new ResearchDirective
            {
                ShouldResearch = true,
                AllowedTools = new List<string> { "GitHubSearchTool", "DocumentationSearchTool" },
                AllowWebSearch = false,
                QueryQuality = "low",
                RecommendedQuery = string.Empty,
                Reasoning = "Research gate failed; using safe default."
            };
        }

        return input;
    }
}
