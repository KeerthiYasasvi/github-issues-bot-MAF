using System.Text.Json;
using System.Text.RegularExpressions;
using SupportConcierge.Core.Agents;

namespace SupportConcierge.Cli.Evals;

/// <summary>
/// Deterministic, offline LLM used for evals and CI.
/// Produces schema-valid JSON with simple heuristics so evals can run without network access.
/// </summary>
public sealed class HeuristicLlmClient : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = string.Join("\n", request.Messages.Select(m => m.Content ?? string.Empty));
        var content = request.SchemaName switch
        {
            "TriageRefinement" => BuildTriage(prompt),
            "ToolSelection" => BuildToolSelection(prompt),
            "ResearchResult" => BuildResearch(prompt),
            "ResponseGeneration" => BuildResponse(prompt),
            "CritiqueResult" => BuildCritique(prompt),
            "OrchestratorPlan" => BuildPlan(prompt),
            "InfoSufficiency" => BuildSufficiency(prompt),
            "OffTopicDecision" => BuildOffTopic(prompt),
            "CasePacket" => BuildCasePacket(request.JsonSchema, prompt),
            "FollowUpGeneration" => BuildFollowUp(prompt),
            "EngineerBrief" => BuildEngineerBrief(prompt),
            _ => "{}"
        };

        return Task.FromResult(new LlmResponse
        {
            IsSuccess = true,
            Content = content,
            RawResponse = content,
            PromptTokens = 0,
            CompletionTokens = 0,
            TotalTokens = 0,
            LatencyMs = 0
        });
    }

    private static string BuildTriage(string prompt)
    {
        var category = DetectCategory(prompt);
        var json = new
        {
            categories = new[] { category },
            custom_category = (object?)null,
            extracted_details = new Dictionary<string, string> { ["title"] = string.Empty },
            confidence_score = 0.9,
            reasoning = $"Matched keywords for {category}"
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildToolSelection(string prompt)
    {
        var category = DetectCategory(prompt);
        var tool = category.Contains("documentation") || category.Contains("docs")
            ? "DocumentationSearchTool"
            : "GitHubSearchTool";
        var json = new
        {
            selected_tools = new[]
            {
                new
                {
                    tool_name = tool,
                    reasoning = $"Primary tool for {category}",
                    query_parameters = new Dictionary<string, string> { ["query"] = "keyword search" }
                }
            },
            investigation_strategy = "Look for relevant repo context",
            expected_findings = new[] { "Relevant references or missing details" }
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildResearch(string prompt)
    {
        var tools = ExtractTools(prompt);
        var json = new
        {
            tools_used = tools,
            findings = new[]
            {
                new
                {
                    finding_type = "documentation",
                    content = "Documentation does not explicitly state the requested detail.",
                    source = tools.FirstOrDefault() ?? "DocumentationSearchTool",
                    confidence = 0.6
                }
            },
            investigation_depth = "shallow",
            next_steps_recommended = new[] { "Request clarification if needed" }
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildResponse(string prompt)
    {
        var category = DetectCategory(prompt);
        var summary = category.Contains("documentation")
            ? "The documentation is missing a specific detail that users need."
            : "The issue needs clarification to proceed.";
        var followUps = BuildFollowUpList(category, prompt);
        var json = new
        {
            brief = new
            {
                title = "Support Response",
                summary,
                solution = "",
                explanation = "Based on initial analysis of the issue text.",
                next_steps = new[] { "Clarify missing details", "Update documentation if confirmed" }
            },
            follow_ups = followUps,
            requires_user_action = true,
            escalation_needed = false
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildCritique(string prompt)
    {
        var failTriage = prompt.Contains("EVAL_FAIL_TRIAGE", StringComparison.OrdinalIgnoreCase);
        var failResearch = prompt.Contains("EVAL_FAIL_RESEARCH", StringComparison.OrdinalIgnoreCase);
        var failResponse = prompt.Contains("EVAL_FAIL_RESPONSE", StringComparison.OrdinalIgnoreCase);
        var shouldFail = failTriage || failResearch || failResponse;

        var json = new
        {
            score = shouldFail ? 3 : 8,
            reasoning = shouldFail ? "Forced critique fail for eval" : "Output is sufficient",
            issues = shouldFail
                ? new[]
                {
                    new { category = "missing_info", problem = "Key details missing", suggestion = "Ask clarifying questions", severity = 4 }
                }
                : Array.Empty<object>(),
            suggestions = shouldFail ? new[] { "Refine output to address missing details" } : Array.Empty<string>(),
            is_passable = !shouldFail
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildPlan(string prompt)
    {
        var json = new
        {
            problem_summary = "Issue needs clarification",
            information_needed = new[] { "Exact location", "Expected behavior" },
            investigation_steps = new[] { "Review issue body", "Search docs", "Ask user for missing details" },
            likely_resolution = true,
            reasoning = "Basic plan generated from issue text"
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildSufficiency(string prompt)
    {
        var missingMatch = Regex.Match(prompt, @"Missing Fields.*?\n(?<missing>.+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var missingText = missingMatch.Success ? missingMatch.Groups["missing"].Value : string.Empty;
        var hasEnough = missingText.TrimStart().StartsWith("none", StringComparison.OrdinalIgnoreCase);
        var json = new
        {
            has_enough_info = hasEnough,
            missing_info = hasEnough ? Array.Empty<string>() : new[] { "doc_location", "issue_description" },
            reasoning = hasEnough ? "Checklist satisfied" : "Checklist missing fields"
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildOffTopic(string prompt)
    {
        var offTopic = prompt.Contains("EVAL_OFF_TOPIC", StringComparison.OrdinalIgnoreCase)
            || (prompt.Contains("Submodule", StringComparison.OrdinalIgnoreCase) && prompt.Contains("README", StringComparison.OrdinalIgnoreCase));
        var json = new
        {
            off_topic = offTopic,
            confidence_score = offTopic ? 0.9 : 0.4,
            reason = offTopic ? "Comment discusses unrelated documentation change" : "Comment aligns with issue topic",
            suggested_action = offTopic ? "redirect_new_issue" : "continue"
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildCasePacket(string? schemaJson, string prompt)
    {
        var required = new List<string>();
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                var schema = JsonSerializer.Deserialize<JsonElement>(schemaJson);
                if (schema.TryGetProperty("required", out var requiredProp))
                {
                    required = requiredProp.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                }
            }
            catch
            {
                required = new List<string>();
            }
        }

        var values = new Dictionary<string, string>();
        foreach (var field in required)
        {
            values[field] = ExtractField(field, prompt);
        }

        return JsonSerializer.Serialize(values);
    }

    private static string BuildFollowUp(string prompt)
    {
        var category = DetectCategory(prompt);
        var followUps = BuildFollowUpList(category, prompt);
        var questions = followUps
            .Select(q => new { question = q, rationale = "Clarify missing context", priority = "high" })
            .ToList<object>();

        var json = new
        {
            follow_up_questions = questions,
            clarification_needed = new[] { "doc_location", "issue_description" },
            additional_context_request = Array.Empty<string>()
        };
        return JsonSerializer.Serialize(json);
    }

    private static string BuildEngineerBrief(string prompt)
    {
        var category = DetectCategory(prompt);
        var summary = category.Contains("documentation")
            ? "Documentation is missing a specific detail needed by users."
            : "Issue requires investigation and potential fix.";
        var nextSteps = category.Contains("build")
            ? new[] { "Confirm build command and log output", "Reproduce the build failure" }
            : new[] { "Verify missing detail", "Confirm expected behavior" };
        var keyEvidence = new[] { "Issue text indicates missing or unclear details." };
        var json = new
        {
            summary = summary,
            symptoms = new[] { "Users cannot find the expected documentation detail." },
            repro_steps = new[] { "Open README", "Locate section", "Verify missing detail" },
            environment = new Dictionary<string, string>(),
            key_evidence = keyEvidence,
            next_steps = nextSteps,
            validation_confirmations = new[] { "Is the missing detail in README?", "Is there a preferred section to update?" }
        };
        return JsonSerializer.Serialize(json);
    }

    private static string[] BuildFollowUpList(string category, string prompt)
    {
        var lowerPrompt = prompt.ToLowerInvariant();
        var list = new List<string>();

        if (category.Contains("documentation"))
        {
            list.Add("Which file or section needs to be updated?");
            list.Add("What is currently incorrect or missing?");
            list.Add("What should the documentation say instead?");
            return list.ToArray();
        }

        if (category.Contains("runtime"))
        {
            list.Add("Can you share the exact error message?");
            list.Add("Can you include the stack trace?");
            list.Add("What are the exact steps to reproduce?");
            return list.ToArray();
        }

        if (category.Contains("build"))
        {
            list.Add("What build command are you running?");
            list.Add("Can you share the build log output?");
            return list.ToArray();
        }

        if (category.Contains("configuration") || lowerPrompt.Contains("api key") || lowerPrompt.Contains("apikey"))
        {
            list.Add("Can you confirm the API key is configured (please do not include the value)?");
            list.Add("Which file or environment variable name is used for the API key?");
            return list.ToArray();
        }

        list.Add("Can you provide more details about the issue?");
        list.Add("What steps lead to the problem?");
        return list.ToArray();
    }

    private static string DetectCategory(string prompt)
    {
        var categoryHint = ExtractLineAfter(prompt, "Categories:")
            ?? ExtractLineAfter(prompt, "Issue Categories:");
        var issueText = ExtractIssueText(prompt);
        var combined = $"{categoryHint}\n{issueText}".ToLowerInvariant();
        var lower = combined;
        if (lower.Contains("readme") || lower.Contains("documentation") || lower.Contains("docs"))
        {
            return "documentation_issue";
        }
        if (lower.Contains("build") || lower.Contains("compile"))
        {
            return "build_issue";
        }
        if (lower.Contains("runtime") || lower.Contains("exception") || lower.Contains("stack trace") || lower.Contains("crash"))
        {
            return "runtime_error";
        }
        if (lower.Contains("feature") || lower.Contains("request"))
        {
            return "feature_request";
        }
        if (lower.Contains("config") || lower.Contains("configuration"))
        {
            return "configuration_error";
        }
        if (lower.Contains("setup") || lower.Contains("install"))
        {
            return "environment_setup";
        }
        return "bug_report";
    }

    private static readonly string[] IssueBodyEndMarkers =
    {
        "\n\nAnalyze",
        "\n\nReturn",
        "\n\nGenerate",
        "\n\nPrevious Classification",
        "\n\nQuality Feedback",
        "\n\nSuggestions",
        "\n\nRefine",
        "\n\nCategories",
        "\n\nIssue Categories",
        "\n\nTools",
        "\n\nExpected",
        "\n\nInvestigation",
        "\n\nResponse",
        "\n\nFollow",
        "\n\nCritique"
    };

    private static string ExtractIssueText(string prompt)
    {
        var normalized = prompt.Replace("\r\n", "\n");
        var title = ExtractLineAfter(normalized, "Issue Title:");
        var body = ExtractSectionAfter(normalized, "Issue Body:", IssueBodyEndMarkers);
        return $"{title}\n{body}".Trim();
    }

    private static string ExtractLineAfter(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var start = index + marker.Length;
        var end = text.IndexOf('\n', start);
        if (end < 0)
        {
            end = text.Length;
        }

        return text.Substring(start, end - start).Trim();
    }

    private static string ExtractSectionAfter(string text, string marker, string[] endMarkers)
    {
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var start = index + marker.Length;
        var end = endMarkers
            .Select(m => text.IndexOf(m, start, StringComparison.OrdinalIgnoreCase))
            .Where(i => i > -1)
            .DefaultIfEmpty(text.Length)
            .Min();

        return text.Substring(start, end - start).Trim();
    }

    private static List<string> ExtractTools(string prompt)
    {
        var tools = new List<string>();
        if (prompt.Contains("DocumentationSearchTool", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add("DocumentationSearchTool");
        }
        if (prompt.Contains("GitHubSearchTool", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add("GitHubSearchTool");
        }
        return tools.Count > 0 ? tools : new List<string> { "DocumentationSearchTool" };
    }

    private static string ExtractField(string field, string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        return field switch
        {
            "operating_system" when lower.Contains("windows") => "Windows",
            "operating_system" when lower.Contains("ubuntu") => "Ubuntu",
            "runtime_version" when Regex.IsMatch(prompt, @"\b\d+\.\d+(\.\d+)?\b") => Regex.Match(prompt, @"\b\d+\.\d+(\.\d+)?\b").Value,
            "build_tool_version" when Regex.IsMatch(prompt, @"\b\d+\.\d+(\.\d+)?\b") => Regex.Match(prompt, @"\b\d+\.\d+(\.\d+)?\b").Value,
            "build_command" when lower.Contains("dotnet build") => "dotnet build",
            "build_command" when lower.Contains("npm") => "npm run build",
            "error_message" when lower.Contains("error") => "error message present",
            "stack_trace" when lower.Contains("stack") => "stack trace present",
            "steps_to_reproduce" when lower.Contains("steps to reproduce") => "steps provided",
            "doc_location" when lower.Contains("readme") => "README.md",
            "issue_description" when lower.Contains("missing") => "Documentation missing details",
            _ => string.Empty
        };
    }
}
