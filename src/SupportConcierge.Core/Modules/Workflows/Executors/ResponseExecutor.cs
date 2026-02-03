using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Modules.Agents;
using SupportConcierge.Core.Modules.Models;

namespace SupportConcierge.Core.Modules.Workflows.Executors;

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
        var keyEvidence = new List<string>();
        if (!string.IsNullOrWhiteSpace(responseResult.Brief.Explanation))
        {
            keyEvidence.Add(responseResult.Brief.Explanation);
        }
        keyEvidence.AddRange(ExtractIssueReferences(investigationResult));

        input.Brief = new EngineerBrief
        {
            Summary = responseResult.Brief.Summary,
            Symptoms = new List<string> { responseResult.Brief.Title },
            Environment = new Dictionary<string, string>(),
            KeyEvidence = keyEvidence,
            NextSteps = responseResult.Brief.NextSteps
        };
        Console.WriteLine($"[MAF] Response: Generated brief - {responseResult.Brief.Summary}");
        if (responseResult.Brief.NextSteps.Count > 0)
        {
            var stepsPreview = string.Join(" | ", responseResult.Brief.NextSteps.Take(3).Select(s => Truncate(s, 120)));
            Console.WriteLine($"[MAF] Response: Next steps preview = {stepsPreview}");
        }

        // Critique response - wrapped in try-catch to prevent workflow termination
        try
        {
            var responseCritique = await _criticAgent.CritiqueResponseAsync(input, input.Brief, null, ct);
            if (!responseCritique.IsPassable)
            {
                Console.WriteLine($"[MAF] Response (Critique): Failed critique (score: {responseCritique.Score}/10), refining...");
                LogCritiqueSummary("Response", responseCritique);
                responseResult = await _responseAgent.RefineAsync(input, triageResult, investigationResult, responseResult, responseCritique, ct);
                input.ResponseRefined = true;
                var refinedEvidence = new List<string>();
                if (!string.IsNullOrWhiteSpace(responseResult.Brief.Explanation))
                {
                    refinedEvidence.Add(responseResult.Brief.Explanation);
                }
                refinedEvidence.AddRange(ExtractIssueReferences(investigationResult));

                input.Brief = new EngineerBrief
                {
                    Summary = responseResult.Brief.Summary,
                    Symptoms = new List<string> { responseResult.Brief.Title },
                    Environment = new Dictionary<string, string>(),
                    KeyEvidence = refinedEvidence,
                    NextSteps = responseResult.Brief.NextSteps
                };
                Console.WriteLine("[MAF] Response: Refined brief");
            }
            else
            {
                Console.WriteLine($"[MAF] Response (Critique): Passed critique (score: {responseCritique.Score}/10)");
            }
        }
        catch (Exception ex)
        {
            // Log the exception but don't fail the workflow - continue with uncritiqued response
            Console.WriteLine($"[MAF] Response (Critique): Exception during critique - {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine("[MAF] Response (Critique): Continuing with uncritiqued response result");
        }

        input.ResponseResult = responseResult;
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

    private static List<string> ExtractIssueReferences(InvestigationResult investigationResult)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var findings = investigationResult.Findings ?? new List<Finding>();
        var pattern = new System.Text.RegularExpressions.Regex(
            @"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/issues/\d+",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var finding in findings)
        {
            var content = $"{finding.Source} {finding.Content}";
            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(content))
            {
                if (!string.IsNullOrWhiteSpace(match.Value))
                {
                    results.Add($"Related issue: {match.Value}");
                }
            }
        }

        return results.ToList();
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

