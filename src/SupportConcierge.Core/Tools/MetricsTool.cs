using System.Diagnostics;
using System.Text.Json;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Tools;

public sealed class MetricsTool
{
    private readonly MetricsRecord _record;
    private readonly Dictionary<string, Stopwatch> _activeTimers = new(StringComparer.OrdinalIgnoreCase);

    public MetricsTool(MetricsRecord record)
    {
        _record = record;
    }

    public MetricsRecord Record => _record;

    public void StartStep(string name)
    {
        if (_activeTimers.ContainsKey(name))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _activeTimers[name] = stopwatch;
    }

    public void EndStep(string name)
    {
        if (!_activeTimers.TryGetValue(name, out var stopwatch))
        {
            return;
        }

        stopwatch.Stop();
        _activeTimers.Remove(name);
        _record.Steps[name] = new StepMetrics { Name = name, DurationMs = stopwatch.Elapsed.TotalMilliseconds };
    }

    public void AddDecision(string key, string value)
    {
        _record.Decisions[key] = value;
    }

    public void AddWarning(string warning)
    {
        _record.Warnings.Add(warning);
    }

    public void AddTokenUsage(int promptTokens, int completionTokens, int totalTokens, double latencyMs)
    {
        _record.TokenUsage.PromptTokens += promptTokens;
        _record.TokenUsage.CompletionTokens += completionTokens;
        _record.TokenUsage.TotalTokens += totalTokens;
        _record.TokenUsage.Calls += 1;
        _record.TokenUsage.TotalLatencyMs += latencyMs;
    }

    public async Task WriteAsync(string directory, string fileName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        _record.CompletedAt = DateTime.UtcNow;
        var path = Path.Combine(directory, fileName);
        var json = JsonSerializer.Serialize(_record, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }
}
