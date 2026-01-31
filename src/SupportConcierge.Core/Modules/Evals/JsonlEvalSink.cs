using System.Text.Json;

namespace SupportConcierge.Core.Modules.Evals;

public sealed class JsonlEvalSink : IAgentEvalSink
{
    private readonly string _outputPath;

    public JsonlEvalSink(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        _outputPath = Path.Combine(outputDir, "agent_eval.jsonl");
    }

    public async Task WriteAsync(AgentEvalRecord record, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(record);
        await File.AppendAllTextAsync(_outputPath, json + Environment.NewLine, cancellationToken);
    }
}

