using System.Text.Json;
using SupportConcierge.Core.Modules.Evals;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Tests;

public sealed class EvalTests
{
    [Fact]
    public void RubricLoader_Loads_Rubric_From_Disk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rubrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var rubric = new RubricDefinition
            {
                RubricId = "triage-test",
                AgentName = "Triage",
                ThresholdScore = 7,
                Items = new List<RubricItem>
                {
                    new() { Id = "secret_request_ban", Description = "No secrets", MaxPoints = 2, Type = "rule", RuleId = "secret_request_ban" }
                }
            };

            var path = Path.Combine(tempDir, "triage.json");
            File.WriteAllText(path, JsonSerializer.Serialize(rubric));

            var loader = new RubricLoader(tempDir);
            var loaded = loader.Load("Triage");

            Assert.Equal("triage-test", loaded.RubricId);
            Assert.Single(loaded.Items);
            Assert.Equal("secret_request_ban", loaded.Items[0].Id);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RubricLoader_Returns_Fallback_When_Missing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rubrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var loader = new RubricLoader(tempDir);
            var loaded = loader.Load("UnknownAgent");

            Assert.Equal("generic", loaded.RubricId);
            Assert.Equal("UnknownAgent", loaded.AgentName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RuleEvaluator_Detects_Tool_AllowList_Violation()
    {
        var rubric = new RubricDefinition
        {
            RubricId = "research-test",
            AgentName = "Research",
            ThresholdScore = 7,
            Items = new List<RubricItem>
            {
                new() { Id = "tool_allowlist", Description = "Tools allowed", MaxPoints = 3, Type = "rule", RuleId = "tool_allowlist" }
            }
        };

        var context = new EvalContext
        {
            ToolAllowList = new[] { "GitHubSearchTool" },
            ToolsUsed = new[] { "GitHubSearchTool", "DocumentationSearchTool" }
        };

        var evaluator = new RuleEvaluator(new SchemaValidator());
        var (subscores, issues, _) = evaluator.Evaluate(rubric, context);

        Assert.Equal(0, subscores["tool_allowlist"]);
        Assert.Contains(issues, i => i.Contains("allow-list", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuleEvaluator_Flags_Secret_Request()
    {
        var rubric = new RubricDefinition
        {
            RubricId = "response-test",
            AgentName = "Response",
            ThresholdScore = 7,
            Items = new List<RubricItem>
            {
                new() { Id = "secret_request_ban", Description = "No secrets", MaxPoints = 3, Type = "rule", RuleId = "secret_request_ban" }
            }
        };

        var context = new EvalContext
        {
            OutputText = "Please share your API key so I can continue."
        };

        var evaluator = new RuleEvaluator(new SchemaValidator());
        var (subscores, issues, _) = evaluator.Evaluate(rubric, context);

        Assert.Equal(0, subscores["secret_request_ban"]);
        Assert.Contains(issues, i => i.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuleEvaluator_Question_Mapping_Detects_Duplicates_And_OverLimit()
    {
        var rubric = new RubricDefinition
        {
            RubricId = "response-test",
            AgentName = "Response",
            ThresholdScore = 7,
            Items = new List<RubricItem>
            {
                new() { Id = "question_mapping", Description = "Questions map to missing fields", MaxPoints = 2, Type = "rule", RuleId = "question_mapping" }
            }
        };

        var context = new EvalContext
        {
            MissingFields = new List<string> { "config_path" },
            FollowUpQuestions = new List<FollowUpQuestion>
            {
                new() { Field = "config_path", Question = "Where is the config_path located?" },
                new() { Field = "config_path", Question = "Where is the config_path located?" },
                new() { Field = "config_path", Question = "What is the config_path value?" },
                new() { Field = "config_path", Question = "Which directory is your config_path in?" }
            }
        };

        var evaluator = new RuleEvaluator(new SchemaValidator());
        var (subscores, issues, _) = evaluator.Evaluate(rubric, context);

        Assert.Equal(0, subscores["question_mapping"]);
        Assert.True(issues.Count >= 1);
    }
}

