using System.Text.Json;

namespace SupportConcierge.Core.Modules.Evals;

public sealed class RubricLoader
{
    private readonly Dictionary<string, RubricDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rubricsDir;

    public RubricLoader(string? rubricsDir = null)
    {
        _rubricsDir = ResolveRubricsDir(rubricsDir);
    }

    public RubricDefinition Load(string agentName)
    {
        if (_cache.TryGetValue(agentName, out var cached))
        {
            return cached;
        }

        var fileName = $"{agentName.ToLowerInvariant()}.json";
        var path = Path.Combine(_rubricsDir, fileName);
        if (!File.Exists(path))
        {
            // Fallback to generic rubric if missing
            var fallback = new RubricDefinition
            {
                RubricId = "generic",
                AgentName = agentName,
                ThresholdScore = 7,
                Items = new List<RubricItem>()
            };
            _cache[agentName] = fallback;
            return fallback;
        }

        var json = File.ReadAllText(path);
        var rubric = JsonSerializer.Deserialize<RubricDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new RubricDefinition { AgentName = agentName };
        _cache[agentName] = rubric;
        return rubric;
    }

    private static string ResolveRubricsDir(string? rubricsDir)
    {
        if (!string.IsNullOrWhiteSpace(rubricsDir) && Directory.Exists(rubricsDir))
        {
            return rubricsDir;
        }

        var env = Environment.GetEnvironmentVariable("SUPPORTBOT_RUBRICS_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return env;
        }

        var cwd = Directory.GetCurrentDirectory();
        var candidate = Path.Combine(cwd, "evals", "rubrics");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        return cwd;
    }
}

