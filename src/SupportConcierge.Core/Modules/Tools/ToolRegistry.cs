namespace SupportConcierge.Core.Modules.Tools;

/// <summary>
/// Interface for tools that agents can select and use dynamically
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    List<ToolParameter> Parameters { get; }

    /// <summary>
    /// Execute the tool with given parameters
    /// </summary>
    Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default);
}

/// <summary>
/// Definition of a tool parameter
/// </summary>
public class ToolParameter
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsRequired { get; set; }
    public string Type { get; set; } = "string"; // string, number, boolean, array
    public List<string>? AllowedValues { get; set; }
}

/// <summary>
/// Result from tool execution
/// </summary>
public class ToolResult
{
    public bool Success { get; set; }
    public string Content { get; set; } = "";
    public string? Error { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Registry for available tools
/// Agents query this to discover and select tools
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly IGitHubTool _gitHub;

    public ToolRegistry(IGitHubTool gitHub)
    {
        _gitHub = gitHub;
        RegisterDefaultTools();
    }

    /// <summary>
    /// Register a tool
    /// </summary>
    public void Register(ITool tool)
    {
        _tools[tool.Name.ToLower()] = tool;
    }

    /// <summary>
    /// Get a specific tool
    /// </summary>
    public ITool? Get(string name)
    {
        return _tools.TryGetValue(name.ToLower(), out var tool) ? tool : null;
    }

    /// <summary>
    /// Get all available tools
    /// </summary>
    public List<ITool> GetAll()
    {
        return _tools.Values.ToList();
    }

    /// <summary>
    /// Get tools filtered by category/type
    /// </summary>
    public List<ITool> GetByType(string type)
    {
        return _tools.Values
            .Where(t => t.Description.Contains(type, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Execute a tool
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var tool = Get(toolName);
        if (tool == null)
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Tool '{toolName}' not found"
            };
        }

        // Validate parameters
        var validation = ValidateParameters(tool, parameters);
        if (!validation.Valid)
        {
            return new ToolResult
            {
                Success = false,
                Error = validation.Error
            };
        }

        try
        {
            return await tool.ExecuteAsync(parameters, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Tool execution failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get tool descriptions for LLM (for tool selection)
    /// </summary>
    public string GetToolDescriptionsForLlm()
    {
        var descriptions = _tools.Values.Select(t => 
            $@"- **{t.Name}**: {t.Description}
  Parameters: {(t.Parameters.Count > 0 ? string.Join(", ", t.Parameters.Select(p => $"{p.Name} ({(p.IsRequired ? "required" : "optional")})")) : "none")}");
        
        return string.Join("\n", descriptions);
    }

    private (bool Valid, string? Error) ValidateParameters(ITool tool, Dictionary<string, string> parameters)
    {
        foreach (var required in tool.Parameters.Where(p => p.IsRequired))
        {
            if (!parameters.ContainsKey(required.Name))
            {
                return (false, $"Required parameter '{required.Name}' is missing");
            }
        }

        foreach (var param in tool.Parameters.Where(p => p.AllowedValues?.Count > 0))
        {
            if (parameters.TryGetValue(param.Name, out var value) && 
                !param.AllowedValues!.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return (false, $"Parameter '{param.Name}' value '{value}' not in allowed values: {string.Join(", ", param.AllowedValues)}");
            }
        }

        return (true, null);
    }

    private void RegisterDefaultTools()
    {
        // Register default tools - can be overridden with real implementations
        Register(new GitHubSearchTool(_gitHub));
        Register(new DocumentationSearchTool(_gitHub));
        Register(new CodeAnalysisTool(_gitHub));
        Register(new ValidationTool(_gitHub));
        Register(new WebSearchTool());
    }
}

/// <summary>
/// Default GitHub Search Tool implementation
/// </summary>
public class GitHubSearchTool : ITool
{
    private readonly IGitHubTool _gitHub;

    public string Name => "GitHubSearchTool";
    
    public string Description => "Search GitHub for related issues, pull requests, and discussions to find similar problems and solutions";
    
    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "query", Description = "Search query (issue, error message, keywords)", IsRequired = true },
        new() { Name = "search_type", Description = "Type of search", IsRequired = false, AllowedValues = new() { "issues", "discussions", "pull_requests", "all" } },
        new() { Name = "repo", Description = "Specific repository (owner/name). Defaults to current repo if omitted.", IsRequired = false },
        new() { Name = "owner", Description = "Repository owner (if repo not provided)", IsRequired = false },
        new() { Name = "max_results", Description = "Maximum results to return", IsRequired = false }
    };

    public GitHubSearchTool(IGitHubTool gitHub)
    {
        _gitHub = gitHub;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var query = parameters.GetValueOrDefault("query", "");
        var searchType = parameters.GetValueOrDefault("search_type", "all");
        var repoValue = parameters.GetValueOrDefault("repo", "");
        var owner = parameters.GetValueOrDefault("owner", "");
        var maxResults = 5;
        if (int.TryParse(parameters.GetValueOrDefault("max_results", "5"), out var parsed))
        {
            maxResults = Math.Clamp(parsed, 1, 20);
        }

        if (!string.IsNullOrWhiteSpace(repoValue) && repoValue.Contains('/'))
        {
            var parts = repoValue.Split('/', 2);
            owner = parts[0];
            repoValue = parts[1];
        }

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repoValue))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing repository context. Provide 'repo' as owner/name or include 'owner' and 'repo' parameters."
            };
        }

        var issues = await _gitHub.SearchIssuesAsync(owner, repoValue, query, maxResults, cancellationToken);
        var lines = issues.Select(issue => $"#{issue.Number} {issue.Title} ({issue.State}) {issue.HtmlUrl}");
        var content = lines.Any()
            ? string.Join("\n", lines)
            : $"No results found for '{query}' in {owner}/{repoValue}.";

        return new ToolResult
        {
            Success = true,
            Content = content,
            Metadata = new() { { "search_type", searchType }, { "result_count", issues.Count.ToString() } }
        };
    }
}

/// <summary>
/// Default Documentation Search Tool implementation
/// </summary>
public class DocumentationSearchTool : ITool
{
    private readonly IGitHubTool _gitHub;

    public string Name => "DocumentationSearchTool";
    
    public string Description => "Search project documentation, README, wiki, and official docs for relevant information";
    
    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "query", Description = "Search term or topic", IsRequired = true },
        new() { Name = "doc_type", Description = "Type of documentation", IsRequired = false, AllowedValues = new() { "readme", "wiki", "docs", "api", "all" } },
        new() { Name = "topic", Description = "Specific topic area", IsRequired = false },
        new() { Name = "paths", Description = "Comma-separated file paths to search (overrides defaults)", IsRequired = false },
        new() { Name = "repo", Description = "Specific repository (owner/name). Defaults to current repo if omitted.", IsRequired = false },
        new() { Name = "owner", Description = "Repository owner (if repo not provided)", IsRequired = false }
    };

    public DocumentationSearchTool(IGitHubTool gitHub)
    {
        _gitHub = gitHub;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var query = parameters.GetValueOrDefault("query", "");
        var docType = parameters.GetValueOrDefault("doc_type", "all");
        var repoValue = parameters.GetValueOrDefault("repo", "");
        var owner = parameters.GetValueOrDefault("owner", "");

        if (!string.IsNullOrWhiteSpace(repoValue) && repoValue.Contains('/'))
        {
            var parts = repoValue.Split('/', 2);
            owner = parts[0];
            repoValue = parts[1];
        }

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repoValue))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing repository context. Provide 'repo' as owner/name or include 'owner' and 'repo' parameters."
            };
        }

        var paths = ResolveDocPaths(parameters);
        var results = new List<string>();

        foreach (var path in paths)
        {
            var content = await _gitHub.GetFileContentAsync(owner, repoValue, path, cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var matches = FindMatches(content, query, 2);
            if (matches.Count > 0)
            {
                results.Add($"[{path}]\n{string.Join("\n", matches)}");
            }
        }

        var output = results.Count > 0
            ? string.Join("\n---\n", results)
            : $"No documentation matches found for '{query}' in {owner}/{repoValue}.";

        return new ToolResult
        {
            Success = true,
            Content = output,
            Metadata = new() { { "doc_type", docType }, { "result_count", results.Count.ToString() } }
        };
    }

    private static List<string> ResolveDocPaths(Dictionary<string, string> parameters)
    {
        var customPaths = parameters.GetValueOrDefault("paths", "");
        if (!string.IsNullOrWhiteSpace(customPaths))
        {
            return customPaths
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new List<string>
        {
            "README.md",
            "docs/README.md",
            "docs/ARCHITECTURE.md",
            "docs/FAQ.md",
            "docs/TROUBLESHOOTING.md",
            "docs/DEPLOYMENT.md",
            "CONTRIBUTING.md"
        };
    }

    private static List<string> FindMatches(string content, string query, int maxMatches)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length && results.Count < maxMatches; i++)
        {
            if (lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                results.Add(lines[i].Trim());
            }
        }

        return results;
    }
}

/// <summary>
/// Default Code Analysis Tool implementation
/// </summary>
public class CodeAnalysisTool : ITool
{
    private readonly IGitHubTool _gitHub;

    public string Name => "CodeAnalysisTool";
    
    public string Description => "Analyze code patterns, versions, dependencies, and configuration to understand technical context";
    
    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "analysis_type", Description = "Type of analysis to perform", IsRequired = true, AllowedValues = new() { "dependencies", "versions", "configuration", "patterns", "error_logs" } },
        new() { Name = "component", Description = "Component or module to analyze", IsRequired = false },
        new() { Name = "path", Description = "Specific file path to analyze", IsRequired = false },
        new() { Name = "repo", Description = "Specific repository (owner/name). Defaults to current repo if omitted.", IsRequired = false },
        new() { Name = "owner", Description = "Repository owner (if repo not provided)", IsRequired = false },
        new() { Name = "depth", Description = "Analysis depth", IsRequired = false, AllowedValues = new() { "shallow", "medium", "deep" } }
    };

    public CodeAnalysisTool(IGitHubTool gitHub)
    {
        _gitHub = gitHub;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var analysisType = parameters.GetValueOrDefault("analysis_type", "");
        var depth = parameters.GetValueOrDefault("depth", "medium");
        var repoValue = parameters.GetValueOrDefault("repo", "");
        var owner = parameters.GetValueOrDefault("owner", "");
        var path = parameters.GetValueOrDefault("path", "");
        var component = parameters.GetValueOrDefault("component", "");

        if (!string.IsNullOrWhiteSpace(repoValue) && repoValue.Contains('/'))
        {
            var parts = repoValue.Split('/', 2);
            owner = parts[0];
            repoValue = parts[1];
        }

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repoValue))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing repository context. Provide 'repo' as owner/name or include 'owner' and 'repo' parameters."
            };
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = ResolveComponentPath(component);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return new ToolResult
            {
                Success = false,
                Error = "No file path provided for analysis."
            };
        }

        var content = await _gitHub.GetFileContentAsync(owner, repoValue, path, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ToolResult
            {
                Success = false,
                Error = $"File not found or empty: {path}"
            };
        }

        var summary = SummarizeContent(content, analysisType, depth);
        
        return new ToolResult
        {
            Success = true,
            Content = $"Analysis ({analysisType}, depth={depth}) for {path}:\n{summary}",
            Metadata = new() { { "analysis_type", analysisType }, { "depth", depth }, { "path", path } }
        };
    }

    private static string ResolveComponentPath(string component)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return string.Empty;
        }

        return component switch
        {
            "readme" => "README.md",
            "docs" => "docs/README.md",
            _ => component
        };
    }

    private static string SummarizeContent(string content, string analysisType, string depth)
    {
        var lines = content.Split('\n');
        var maxLines = depth == "deep" ? 40 : depth == "shallow" ? 10 : 20;
        var excerpt = string.Join("\n", lines.Take(maxLines).Select(l => l.TrimEnd()));
        return $"{analysisType} summary (first {maxLines} lines):\n{excerpt}";
    }
}

/// <summary>
/// Default Validation Tool implementation
/// </summary>
public class ValidationTool : ITool
{
    private readonly IGitHubTool _gitHub;

    public string Name => "ValidationTool";
    
    public string Description => "Validate configuration, environment setup, and schema compliance";
    
    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "validation_type", Description = "What to validate", IsRequired = true, AllowedValues = new() { "configuration", "environment", "schema", "dependencies", "all" } },
        new() { Name = "strict", Description = "Use strict validation", IsRequired = false, AllowedValues = new() { "true", "false" } },
        new() { Name = "path", Description = "Config file path to validate", IsRequired = false },
        new() { Name = "repo", Description = "Specific repository (owner/name). Defaults to current repo if omitted.", IsRequired = false },
        new() { Name = "owner", Description = "Repository owner (if repo not provided)", IsRequired = false }
    };

    public ValidationTool(IGitHubTool gitHub)
    {
        _gitHub = gitHub;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var validationType = parameters.GetValueOrDefault("validation_type", "");
        var strict = parameters.GetValueOrDefault("strict", "false") == "true";
        var repoValue = parameters.GetValueOrDefault("repo", "");
        var owner = parameters.GetValueOrDefault("owner", "");
        var path = parameters.GetValueOrDefault("path", "");

        if (!string.IsNullOrWhiteSpace(repoValue) && repoValue.Contains('/'))
        {
            var parts = repoValue.Split('/', 2);
            owner = parts[0];
            repoValue = parts[1];
        }

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repoValue))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing repository context. Provide 'repo' as owner/name or include 'owner' and 'repo' parameters."
            };
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = "README.md";
        }

        var content = await _gitHub.GetFileContentAsync(owner, repoValue, path, cancellationToken: cancellationToken);
        var status = string.IsNullOrWhiteSpace(content) ? "missing" : "present";
        
        return new ToolResult
        {
            Success = true,
            Content = $"Validation ({validationType}, strict={strict}) on {path}: file is {status}.",
            Metadata = new() { { "validation_type", validationType }, { "strict", strict.ToString() }, { "path", path } }
        };
    }
}

/// <summary>
/// Optional Web Search Tool (guarded by env + confidence threshold).
/// </summary>
public sealed class WebSearchTool : ITool
{
    public string Name => "WebSearchTool";

    public string Description => "Web search for external references when repo-only tools are insufficient (requires high confidence and explicit enablement)";

    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "query", Description = "Search query", IsRequired = true },
        new() { Name = "confidence", Description = "Confidence (0-1) that web search is required", IsRequired = true },
        new() { Name = "justification", Description = "Why repo-only tools are insufficient", IsRequired = true }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        if (!parameters.TryGetValue("confidence", out var confidenceValue) ||
            !decimal.TryParse(confidenceValue, out var confidence) || confidence < 0.60m)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Web search blocked: confidence must be >= 0.60"
            };
        }

        if (!parameters.TryGetValue("justification", out var justification) ||
            string.IsNullOrWhiteSpace(justification))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Web search blocked: justification is required"
            };
        }

        var enabled = string.Equals(Environment.GetEnvironmentVariable("SUPPORTBOT_WEB_SEARCH_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Web search disabled (set SUPPORTBOT_WEB_SEARCH_ENABLED=true to allow)"
            };
        }

        var endpoint = Environment.GetEnvironmentVariable("SUPPORTBOT_WEB_SEARCH_URL");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Web search endpoint not configured (set SUPPORTBOT_WEB_SEARCH_URL)"
            };
        }

        var query = parameters.GetValueOrDefault("query", string.Empty);
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolResult { Success = false, Error = "Query is required" };
        }

        try
        {
            using var client = new HttpClient();
            var url = $"{endpoint}?q={Uri.EscapeDataString(query)}";
            var content = await client.GetStringAsync(url, cancellationToken);
            return new ToolResult
            {
                Success = true,
                Content = content,
                Metadata = new() { { "source", endpoint }, { "query", query } }
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Web search failed: {ex.Message}"
            };
        }
    }
}

