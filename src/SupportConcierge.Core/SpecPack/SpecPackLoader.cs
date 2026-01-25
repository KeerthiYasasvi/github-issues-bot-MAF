using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SupportConcierge.Core.SpecPack;

public sealed class SpecPackLoader : SupportConcierge.Core.Tools.ISpecPackLoader
{
    private readonly string _specDir;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ISerializer _yamlSerializer;

    public SpecPackLoader(string? specDir = null)
    {
        _specDir = specDir ?? Environment.GetEnvironmentVariable("SUPPORTBOT_SPEC_DIR") ?? ".supportbot";
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public async Task<SpecPackConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        var config = new SpecPackConfig();

        var categoriesPath = Path.Combine(_specDir, "categories.yaml");
        if (File.Exists(categoriesPath))
        {
            var yaml = await File.ReadAllTextAsync(categoriesPath, cancellationToken);
            var data = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yaml) ?? new();
            if (data.TryGetValue("categories", out var categoriesNode))
            {
                var categoriesYaml = _yamlSerializer.Serialize(categoriesNode);
                config.Categories = _yamlDeserializer.Deserialize<List<Category>>(categoriesYaml) ?? new();
            }
        }

        var checklistsPath = Path.Combine(_specDir, "checklists.yaml");
        if (File.Exists(checklistsPath))
        {
            var yaml = await File.ReadAllTextAsync(checklistsPath, cancellationToken);
            var data = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yaml) ?? new();
            if (data.TryGetValue("checklists", out var checklistsNode))
            {
                if (checklistsNode is List<object> list)
                {
                    foreach (var entry in list)
                    {
                        var checklistYaml = _yamlSerializer.Serialize(entry);
                        var checklist = _yamlDeserializer.Deserialize<CategoryChecklist>(checklistYaml);
                        if (checklist != null && !string.IsNullOrWhiteSpace(checklist.Category))
                        {
                            config.Checklists[checklist.Category] = checklist;
                        }
                    }
                }
            }
        }

        var validatorsPath = Path.Combine(_specDir, "validators.yaml");
        if (File.Exists(validatorsPath))
        {
            var yaml = await File.ReadAllTextAsync(validatorsPath, cancellationToken);
            config.Validators = _yamlDeserializer.Deserialize<ValidatorRules>(yaml) ?? new();
        }

        var routingPath = Path.Combine(_specDir, "routing.yaml");
        if (File.Exists(routingPath))
        {
            var yaml = await File.ReadAllTextAsync(routingPath, cancellationToken);
            config.Routing = _yamlDeserializer.Deserialize<RoutingRules>(yaml) ?? new();
        }

        var playbooksDir = Path.Combine(_specDir, "playbooks");
        if (Directory.Exists(playbooksDir))
        {
            var playbookFiles = Directory.GetFiles(playbooksDir, "*.md");
            foreach (var file in playbookFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                config.Playbooks[name] = content;
            }
        }

        ValidateConfig(config);
        return config;
    }

    private static void ValidateConfig(SpecPackConfig config)
    {
        var categoryNames = new HashSet<string>(config.Categories.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var checklist in config.Checklists.Values)
        {
            if (!categoryNames.Contains(checklist.Category))
            {
                throw new InvalidOperationException($"Checklist category '{checklist.Category}' not defined in categories.yaml");
            }
        }
    }
}
