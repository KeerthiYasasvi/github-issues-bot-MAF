namespace SupportConcierge.Core.Tools;

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

    public ToolRegistry()
    {
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
        Register(new GitHubSearchTool());
        Register(new DocumentationSearchTool());
        Register(new CodeAnalysisTool());
        Register(new ValidationTool());
    }
}

/// <summary>
/// Default GitHub Search Tool implementation
/// </summary>
public class GitHubSearchTool : ITool
{
    public string Name => "GitHubSearchTool";
    
    public string Description => "Search GitHub for related issues, pull requests, and discussions to find similar problems and solutions";
    
    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "query", Description = "Search query (issue, error message, keywords)", IsRequired = true },
        new() { Name = "search_type", Description = "Type of search", IsRequired = false, AllowedValues = new() { "issues", "discussions", "pull_requests", "all" } },
        new() { Name = "repo", Description = "Specific repository or 'all' for cross-repo", IsRequired = false }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        // This would integrate with actual GitHub API
        // For now, return placeholder
        var query = parameters.GetValueOrDefault("query", "");
        var searchType = parameters.GetValueOrDefault("search_type", "all");
        
        return await Task.FromResult(new ToolResult
        {
            Success = true,
            Content = $"GitHub search results for '{query}' ({searchType}): [3 similar issues found]",
            Metadata = new() { { "search_type", searchType }, { "result_count", "3" } }
        });
    }
}

/// <summary>
/// Default Documentation Search Tool implementation
/// </summary>
public class DocumentationSearchTool : ITool
{
    public string Name => "DocumentationSearchTool";
    
    public string Description => "Search project documentation, README, wiki, and official docs for relevant information";
    
    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "query", Description = "Search term or topic", IsRequired = true },
        new() { Name = "doc_type", Description = "Type of documentation", IsRequired = false, AllowedValues = new() { "readme", "wiki", "docs", "api", "all" } },
        new() { Name = "topic", Description = "Specific topic area", IsRequired = false }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var query = parameters.GetValueOrDefault("query", "");
        var docType = parameters.GetValueOrDefault("doc_type", "all");
        
        return await Task.FromResult(new ToolResult
        {
            Success = true,
            Content = $"Documentation search results for '{query}' ({docType}): [2 relevant documents found]",
            Metadata = new() { { "doc_type", docType }, { "result_count", "2" } }
        });
    }
}

/// <summary>
/// Default Code Analysis Tool implementation
/// </summary>
public class CodeAnalysisTool : ITool
{
    public string Name => "CodeAnalysisTool";
    
    public string Description => "Analyze code patterns, versions, dependencies, and configuration to understand technical context";
    
    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "analysis_type", Description = "Type of analysis to perform", IsRequired = true, AllowedValues = new() { "dependencies", "versions", "configuration", "patterns", "error_logs" } },
        new() { Name = "component", Description = "Component or module to analyze", IsRequired = false },
        new() { Name = "depth", Description = "Analysis depth", IsRequired = false, AllowedValues = new() { "shallow", "medium", "deep" } }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var analysisType = parameters.GetValueOrDefault("analysis_type", "");
        var depth = parameters.GetValueOrDefault("depth", "medium");
        
        return await Task.FromResult(new ToolResult
        {
            Success = true,
            Content = $"Code analysis results ({analysisType}, depth={depth}): [Analysis details here]",
            Metadata = new() { { "analysis_type", analysisType }, { "depth", depth } }
        });
    }
}

/// <summary>
/// Default Validation Tool implementation
/// </summary>
public class ValidationTool : ITool
{
    public string Name => "ValidationTool";
    
    public string Description => "Validate configuration, environment setup, and schema compliance";
    
    public List<ToolParameter> Parameters => new()
    {
        new() { Name = "validation_type", Description = "What to validate", IsRequired = true, AllowedValues = new() { "configuration", "environment", "schema", "dependencies", "all" } },
        new() { Name = "strict", Description = "Use strict validation", IsRequired = false, AllowedValues = new() { "true", "false" } }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var validationType = parameters.GetValueOrDefault("validation_type", "");
        var strict = parameters.GetValueOrDefault("strict", "false") == "true";
        
        return await Task.FromResult(new ToolResult
        {
            Success = true,
            Content = $"Validation results ({validationType}, strict={strict}): [Validation details here]",
            Metadata = new() { { "validation_type", validationType }, { "strict", strict.ToString() } }
        });
    }
}
