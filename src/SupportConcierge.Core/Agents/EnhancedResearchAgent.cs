using System.Text.Json;
using System.Text.Json.Serialization;
using SupportConcierge.Core.Models;
using SupportConcierge.Core.Prompts;
using SupportConcierge.Core.Schemas;
using SupportConcierge.Core.Tools;

namespace SupportConcierge.Core.Agents;

/// <summary>
/// Enhanced ResearchAgent with dynamic tool selection.
/// Agents select tools based on the investigation strategy, not hardcoded calls.
/// 
/// Responsibilities:
/// - Select appropriate tools for investigation
/// - Conduct investigation using selected tools
/// - Respond to critique feedback with deeper dives
/// - Return structured findings
/// </summary>
public class EnhancedResearchAgent
{
    private readonly ILlmClient _llmClient;
    private readonly SchemaValidator _schemaValidator;

    public EnhancedResearchAgent(ILlmClient llmClient, SchemaValidator schemaValidator)
    {
        _llmClient = llmClient;
        _schemaValidator = schemaValidator;
    }

    /// <summary>
    /// Determine which tools to use for investigation
    /// </summary>
    public async Task<ToolSelectionResult> SelectToolsAsync(
        RunContext context,
        TriageResult triageResult,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetToolSelectionSchema();

        var availableTools = string.Join(", ", new[]
        {
            "GitHubSearchTool (search issues, pull requests, discussions)",
            "DocumentationSearchTool (search docs, readme, wiki)",
            "CodeAnalysisTool (analyze code patterns, version info)",
            "ValidationTool (check configuration, environment setup)",
            "CommentPostTool (analyze existing comments for context)",
            "LabelApplyTool (check issue labels for categorization hints)"
        });

        var categoriesText = string.Join(", ", triageResult.Categories);
        var extractedText = string.Join("; ", triageResult.ExtractedDetails.Select(kv => $"{kv.Key}: {kv.Value}"));

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "research-select-tools.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["CATEGORIES"] = categoriesText,
                ["EXTRACTED_DETAILS"] = extractedText,
                ["AVAILABLE_TOOLS"] = availableTools
            },
            cancellationToken);

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            },
            JsonSchema = schema,
            SchemaName = "ToolSelection",
            Temperature = 0.3
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseToolSelectionResponse(response);
    }

    /// <summary>
    /// Conduct investigation using selected tools
    /// </summary>
    public async Task<InvestigationResult> InvestigateAsync(
        RunContext context,
        TriageResult triageResult,
        ToolSelectionResult toolSelection,
        Dictionary<string, string> toolResults,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetResearchResultSchema();

        var toolsUsedText = string.Join(", ", toolSelection.SelectedTools.Select(t => t.ToolName));
        var resultsText = string.Join("\n---\n", toolResults.Select(kv => $"[{kv.Key}]\n{kv.Value}"));

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "research-investigate.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["CATEGORIES"] = string.Join(", ", triageResult.Categories),
                ["INVESTIGATION_STRATEGY"] = toolSelection.InvestigationStrategy ?? string.Empty,
                ["TOOLS_USED"] = toolsUsedText,
                ["TOOL_RESULTS"] = resultsText
            },
            cancellationToken);

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            },
            JsonSchema = schema,
            SchemaName = "ResearchResult",
            Temperature = 0.4
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseInvestigationResponse(response);
    }

    /// <summary>
    /// Conduct deeper investigation when critique feedback indicates gaps
    /// </summary>
    public async Task<InvestigationResult> DeepDiveAsync(
        RunContext context,
        TriageResult triageResult,
        InvestigationResult previousInvestigation,
        CritiqueResult critiqueFeedback,
        Dictionary<string, string> additionalToolResults,
        CancellationToken cancellationToken = default)
    {
        var schema = OrchestrationSchemas.GetResearchResultSchema();

        var issuesText = string.Join("\n", critiqueFeedback.Issues
            .Select(i => $"- [{i.Severity}/5] {i.Category}: {i.Problem}"));

        var previousFindingsText = string.Join("\n", previousInvestigation.Findings
            .Select(f => $"- {f.FindingType}: {f.Content} (confidence: {f.Confidence})"));

        var additionalText = string.Join("\n---\n", additionalToolResults
            .Select(kv => $"[{kv.Key}]\n{kv.Value}"));

        var (systemPrompt, userPrompt) = await MafPromptTemplates.LoadAsync(
            "research-deep-dive.md",
            new Dictionary<string, string>
            {
                ["ISSUE_TITLE"] = context.Issue.Title ?? string.Empty,
                ["CATEGORIES"] = string.Join(", ", triageResult.Categories),
                ["PREV_INVESTIGATION_DEPTH"] = previousInvestigation.InvestigationDepth ?? string.Empty,
                ["PREV_FINDINGS"] = previousFindingsText,
                ["CRITIQUE_SCORE"] = critiqueFeedback.Score.ToString("0.##"),
                ["CRITIQUE_ISSUES"] = issuesText,
                ["ADDITIONAL_TOOL_RESULTS"] = additionalText
            },
            cancellationToken);

        var request = new LlmRequest
        {
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            },
            JsonSchema = schema,
            SchemaName = "ResearchResult",
            Temperature = 0.4
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        return ParseInvestigationResponse(response);
    }

    private ToolSelectionResult ParseToolSelectionResponse(LlmResponse response)
    {
        var defaultResult = new ToolSelectionResult
        {
            SelectedTools = new List<SelectedTool>
            {
                new()
                {
                    ToolName = "GitHubSearchTool",
                    Reasoning = "Default search for similar issues",
                    QueryParameters = new Dictionary<string, string>()
                }
            },
            InvestigationStrategy = "Basic issue analysis",
            ExpectedFindings = new List<string> { "Similar issues for comparison" }
        };

        if (!response.IsSuccess)
        {
            return defaultResult;
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);

            var selectedTools = json.TryGetProperty("selected_tools", out var toolsProperty)
                ? toolsProperty.EnumerateArray()
                    .Select(t => new SelectedTool
                    {
                        ToolName = t.GetProperty("tool_name").GetString() ?? "",
                        Reasoning = t.GetProperty("reasoning").GetString() ?? "",
                        QueryParameters = t.TryGetProperty("query_parameters", out var qp)
                            ? qp.EnumerateObject()
                                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Value.ToString())
                            : new Dictionary<string, string>()
                    })
                    .ToList()
                : new List<SelectedTool>();

            var expectedFindings = json.TryGetProperty("expected_findings", out var expectedProperty)
                ? expectedProperty.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList()
                : new List<string>();

            return new ToolSelectionResult
            {
                SelectedTools = selectedTools.Count > 0 ? selectedTools : defaultResult.SelectedTools,
                InvestigationStrategy = json.GetProperty("investigation_strategy").GetString() ?? "Issue analysis",
                ExpectedFindings = expectedFindings
            };
        }
        catch
        {
            return defaultResult;
        }
    }

    private InvestigationResult ParseInvestigationResponse(LlmResponse response)
    {
        var defaultResult = new InvestigationResult
        {
            ToolsUsed = new List<string>(),
            Findings = new List<Finding>
            {
                new()
                {
                    FindingType = "error",
                    Content = "Unable to parse investigation results",
                    Source = "system",
                    Confidence = 0.1m
                }
            },
            InvestigationDepth = "shallow",
            NextStepsRecommended = new List<string> { "Retry investigation" }
        };

        if (!response.IsSuccess)
        {
            return defaultResult;
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);

            var toolsUsed = json.TryGetProperty("tools_used", out var toolsProperty)
                ? toolsProperty.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList()
                : new List<string>();

            var findings = json.TryGetProperty("findings", out var findingsProperty)
                ? findingsProperty.EnumerateArray()
                    .Select(f => new Finding
                    {
                        FindingType = f.GetProperty("finding_type").GetString() ?? "unknown",
                        Content = f.GetProperty("content").GetString() ?? "",
                        Source = f.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
                        Confidence = f.TryGetProperty("confidence", out var conf) ? conf.GetDecimal() : 0.5m
                    })
                    .ToList()
                : new List<Finding>();

            var nextSteps = json.TryGetProperty("next_steps_recommended", out var nextProperty)
                ? nextProperty.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList()
                : new List<string>();

            return new InvestigationResult
            {
                ToolsUsed = toolsUsed,
                Findings = findings.Count > 0 ? findings : defaultResult.Findings,
                InvestigationDepth = json.GetProperty("investigation_depth").GetString() ?? "shallow",
                NextStepsRecommended = nextSteps
            };
        }
        catch
        {
            return defaultResult;
        }
    }
}

/// <summary>
/// Result of tool selection
/// </summary>
public class ToolSelectionResult
{
    public List<SelectedTool> SelectedTools { get; set; } = new();
    public string InvestigationStrategy { get; set; } = "";
    public List<string> ExpectedFindings { get; set; } = new();
}

/// <summary>
/// Tool selected for investigation
/// </summary>
public class SelectedTool
{
    public string ToolName { get; set; } = "";
    public string Reasoning { get; set; } = "";
    public Dictionary<string, string> QueryParameters { get; set; } = new();
}

/// <summary>
/// Result of investigation
/// </summary>
public class InvestigationResult
{
    public List<string> ToolsUsed { get; set; } = new();
    public List<Finding> Findings { get; set; } = new();
    public string InvestigationDepth { get; set; } = "shallow"; // shallow, medium, deep
    public List<string> NextStepsRecommended { get; set; } = new();
}

/// <summary>
/// Individual finding from investigation
/// </summary>
public class Finding
{
    [JsonPropertyName("finding_type")]
    public string FindingType { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; } // 0-1
}
