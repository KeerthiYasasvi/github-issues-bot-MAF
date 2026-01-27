using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Response - generate brief, critique and refine if needed
/// </summary>
public sealed class ResponseExecutor : Executor<RunContext, RunContext>
{
    private readonly EnhancedResponseAgent _responseAgent;
    private readonly CriticAgent _criticAgent;

    public ResponseExecutor(EnhancedResponseAgent responseAgent, CriticAgent criticAgent)
        : base("response", ExecutorDefaults.Options, false)
    {
        _responseAgent = responseAgent;
        _criticAgent = criticAgent;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        Console.WriteLine("[MAF] Response: Starting response generation");

        var triageResult = input.TriageResult ?? new TriageResult();
        var investigationResult = input.InvestigationResult ?? new InvestigationResult();

        // Generate response
        var responseResult = await _responseAgent.GenerateResponseAsync(input, triageResult, investigationResult, ct);
        input.Brief = new EngineerBrief
        {
            Summary = responseResult.Brief.Summary,
            Symptoms = new List<string> { responseResult.Brief.Title },
            Environment = new Dictionary<string, string>(),
            KeyEvidence = new List<string> { responseResult.Brief.Explanation },
            NextSteps = responseResult.Brief.NextSteps
        };
        Console.WriteLine($"[MAF] Response: Generated brief - {responseResult.Brief.Summary}");

        // Critique response
        var responseCritique = await _criticAgent.CritiqueResponseAsync(input, input.Brief, null, ct);
        if (!responseCritique.IsPassable)
        {
            Console.WriteLine($"[MAF] Response (Critique): Failed critique (score: {responseCritique.Score}/10), refining...");
            responseResult = await _responseAgent.RefineAsync(input, triageResult, investigationResult, responseResult, responseCritique, ct);
            input.Brief = new EngineerBrief
            {
                Summary = responseResult.Brief.Summary,
                Symptoms = new List<string> { responseResult.Brief.Title },
                Environment = new Dictionary<string, string>(),
                KeyEvidence = new List<string> { responseResult.Brief.Explanation },
                NextSteps = responseResult.Brief.NextSteps
            };
            Console.WriteLine("[MAF] Response: Refined brief");
        }
        else
        {
            Console.WriteLine($"[MAF] Response (Critique): Passed critique (score: {responseCritique.Score}/10)");
        }

        input.ResponseResult = responseResult;
        return input;
    }
}
