using System.Text.Json;

namespace SupportConcierge.Core.Schemas;

/// <summary>
/// Orchestration-related JSON schemas for LLM function calls
/// </summary>
public static class OrchestrationSchemas
{
    /// <summary>
    /// Schema for OrchestratorAgent plan generation
    /// </summary>
    public static string GetPlanSchema()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                problem_summary = new
                {
                    type = "string",
                    description = "Brief, clear understanding of the GitHub issue"
                },
                information_needed = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Key information/details needed to investigate"
                },
                investigation_steps = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Ordered investigation steps to take"
                },
                likely_resolution = new
                {
                    type = "boolean",
                    description = "Can this issue likely be resolved with investigation?"
                },
                reasoning = new
                {
                    type = "string",
                    description = "Reasoning for the assessment"
                }
            },
            required = new[] { "problem_summary", "information_needed", "investigation_steps", "likely_resolution", "reasoning" }
        };

        return JsonSerializer.Serialize(schema);
    }

    /// <summary>
    /// Schema for CriticAgent critique results
    /// </summary>
    public static string GetCritiqueResultSchema()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                score = new
                {
                    type = "number",
                    description = "Score from 1-10 (1=poor, 10=excellent)",
                    minimum = 1,
                    maximum = 10
                },
                reasoning = new
                {
                    type = "string",
                    description = "Summary of why this score was assigned"
                },
                issues = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            category = new
                            {
                                type = "string",
                                description = "Category: hallucination, low_confidence, missing_info, unclear, tone, accuracy_error, incomplete"
                            },
                            problem = new
                            {
                                type = "string",
                                description = "Specific problem found"
                            },
                            suggestion = new
                            {
                                type = "string",
                                description = "Actionable suggestion to fix this issue"
                            },
                            severity = new
                            {
                                type = "integer",
                                description = "Severity 1-5 (1=minor, 5=critical)",
                                minimum = 1,
                                maximum = 5
                            }
                        },
                        required = new[] { "category", "problem", "suggestion", "severity" }
                    },
                    description = "Array of specific issues found"
                },
                suggestions = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "General suggestions for improvement (not tied to specific issues)"
                },
                is_passable = new
                {
                    type = "boolean",
                    description = "Boolean: true if score >= threshold for this stage"
                }
            },
            required = new[] { "score", "reasoning", "issues", "suggestions", "is_passable" }
        };

        return JsonSerializer.Serialize(schema);
    }

    /// <summary>
    /// Schema for TriageAgent refinement after critique feedback
    /// </summary>
    public static string GetTriageRefinementSchema()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                categories = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Refined list of issue categories"
                },
                custom_category = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        description = new { type = "string" },
                        required_fields = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        }
                    },
                    description = "Custom category if confidence < 0.75 (optional)"
                },
                extracted_details = new
                {
                    type = "object",
                    additionalProperties = true,
                    description = "Refined extracted details from issue"
                },
                confidence_score = new
                {
                    type = "number",
                    minimum = 0,
                    maximum = 1,
                    description = "Confidence 0-1"
                },
                reasoning = new
                {
                    type = "string",
                    description = "Why these categories were assigned"
                }
            },
            required = new[] { "categories", "extracted_details", "confidence_score", "reasoning" }
        };

        return JsonSerializer.Serialize(schema);
    }

    /// <summary>
    /// Schema for ResearchAgent investigation results
    /// </summary>
    public static string GetResearchResultSchema()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                tools_used = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Tools selected and used for investigation"
                },
                findings = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            finding_type = new { type = "string" },
                            content = new { type = "string" },
                            source = new { type = "string" },
                            confidence = new { type = "number", minimum = 0, maximum = 1 }
                        }
                    },
                    description = "Structured findings from investigation"
                },
                investigation_depth = new
                {
                    type = "string",
                    @enum = new[] { "shallow", "medium", "deep" },
                    description = "How thorough was the investigation?"
                },
                next_steps_recommended = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Recommended next investigation steps if needed"
                }
            },
            required = new[] { "tools_used", "findings", "investigation_depth" }
        };

        return JsonSerializer.Serialize(schema);
    }

    /// <summary>
    /// Schema for ResponseAgent brief/response generation
    /// </summary>
    public static string GetResponseGenerationSchema()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                brief = new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string" },
                        summary = new { type = "string" },
                        solution = new { type = "string" },
                        explanation = new { type = "string" },
                        next_steps = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        }
                    },
                    description = "Generated response brief"
                },
                follow_ups = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Follow-up questions to ask, if any"
                },
                requires_user_action = new
                {
                    type = "boolean",
                    description = "Does user need to take action?"
                },
                escalation_needed = new
                {
                    type = "boolean",
                    description = "Should this be escalated to a human?"
                }
            },
            required = new[] { "brief", "follow_ups", "requires_user_action" }
        };

        return JsonSerializer.Serialize(schema);
    }

    /// <summary>
    /// Schema for tool selection by ResearchAgent
    /// </summary>
    public static string GetToolSelectionSchema()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                selected_tools = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            tool_name = new { type = "string" },
                            reasoning = new { type = "string" },
                            query_parameters = new
                            {
                                type = "object",
                                additionalProperties = true
                            }
                        }
                    },
                    description = "Tools to use for investigation"
                },
                investigation_strategy = new
                {
                    type = "string",
                    description = "Overall strategy for this investigation"
                },
                expected_findings = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "What we hope to find"
                }
            },
            required = new[] { "selected_tools", "investigation_strategy" }
        };

        return JsonSerializer.Serialize(schema);
    }
}
