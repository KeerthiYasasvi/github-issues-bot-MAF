using System.Text.Json;
using System.Text;

namespace SupportConcierge.Core.Modules.Evals;

public sealed class AgentEvalAggregator
{
    public void GenerateReports(string evalDir)
    {
        var path = Path.Combine(evalDir, "agent_eval.jsonl");
        if (!File.Exists(path))
        {
            return;
        }

        var records = File.ReadAllLines(path)
            .Select(line => JsonSerializer.Deserialize<AgentEvalRecord>(line))
            .Where(r => r != null)
            .Cast<AgentEvalRecord>()
            .ToList();

        if (records.Count == 0)
        {
            return;
        }

        var perAgent = records.GroupBy(r => r.AgentName)
            .ToDictionary(g => g.Key, g => new
            {
                avg_score = g.Average(r => r.Judgement.ScoreOverall),
                pass_rate = g.Count(r => r.Judgement.PassedThreshold) / (double)g.Count(),
                p95_duration_ms = Percentile(g.Select(r => (double)r.DurationMs).ToList(), 0.95),
                avg_tokens = g.Average(r => r.TotalTokens),
                top_issues = g.SelectMany(r => r.Judgement.Issues).GroupBy(i => i).OrderByDescending(x => x.Count()).Take(5).Select(x => x.Key).ToList()
            });

        var summary = new
        {
            per_agent = perAgent
        };

        var summaryJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(evalDir, "agent_eval_summary.json"), summaryJson);

        var md = new StringBuilder();
        md.AppendLine("# Agent Eval Summary");
        md.AppendLine();
        md.AppendLine("| Agent | Avg Score | Pass Rate | P95 Duration (ms) | Avg Tokens |");
        md.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var (agent, stats) in perAgent)
        {
            md.AppendLine($"| {agent} | {stats.avg_score:0.00} | {stats.pass_rate:P0} | {stats.p95_duration_ms:0} | {stats.avg_tokens:0} |");
        }

        File.WriteAllText(Path.Combine(evalDir, "AGENT_EVAL_SUMMARY.md"), md.ToString());

        WriteBotPerformanceReport(evalDir, records);
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        var index = (int)Math.Ceiling(percentile * values.Count) - 1;
        index = Math.Max(0, Math.Min(values.Count - 1, index));
        return values[index];
    }

    private static void WriteBotPerformanceReport(
        string evalDir,
        List<AgentEvalRecord> records)
    {
        var total = records.Count;
        var avgTokens = records.Average(r => r.TotalTokens);
        var reliability = records.Count(r => !r.Judgement.Issues.Any(i => i.Contains("schema", StringComparison.OrdinalIgnoreCase)));
        var reliabilityRate = total == 0 ? 0 : reliability / (double)total;
        var secretViolations = records.Count(r => r.Judgement.Issues.Any(i => i.Contains("secret", StringComparison.OrdinalIgnoreCase)));

        var responseScores = records.Where(r => r.AgentName.Equals("Response", StringComparison.OrdinalIgnoreCase)).Select(r => r.Judgement.ScoreOverall).ToList();
        var researchScores = records.Where(r => r.AgentName.Equals("Research", StringComparison.OrdinalIgnoreCase)).Select(r => r.Judgement.ScoreOverall).ToList();
        var triageScores = records.Where(r => r.AgentName.Equals("Triage", StringComparison.OrdinalIgnoreCase)).Select(r => r.Judgement.ScoreOverall).ToList();

        var md = new StringBuilder();
        md.AppendLine("# Bot Performance");
        md.AppendLine();
        md.AppendLine("| Metric | Value |");
        md.AppendLine("| --- | --- |");
        md.AppendLine($"| Quality (avg Triage) | {(triageScores.Count == 0 ? 0 : triageScores.Average()):0.00} |");
        md.AppendLine($"| Quality (avg Research) | {(researchScores.Count == 0 ? 0 : researchScores.Average()):0.00} |");
        md.AppendLine($"| Quality (avg Response) | {(responseScores.Count == 0 ? 0 : responseScores.Average()):0.00} |");
        md.AppendLine($"| Safety (secret violations) | {secretViolations} |");
        md.AppendLine($"| Reliability (schema ok rate) | {reliabilityRate:P0} |");
        md.AppendLine($"| Efficiency (avg tokens per agent run) | {avgTokens:0} |");
        md.AppendLine();
        md.AppendLine("Note: E2E outcome rates require issue decision telemetry and are not derived from per-agent eval logs.");

        File.WriteAllText(Path.Combine(evalDir, "BOT_PERFORMANCE.md"), md.ToString());
    }
}

