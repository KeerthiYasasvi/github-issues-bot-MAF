using SupportConcierge.Models;

namespace SupportConcierge.Prompts;

/// <summary>
/// Runtime-loaded prompt system. Prompts are loaded from template files at runtime,
/// allowing modification without recompilation.
/// </summary>
public static class Prompts
{
    private static readonly string PromptsDirectory = GetPromptsDirectory();

    public static async Task<string> CategoryClassificationAsync(string title, string body, string categories)
    {
        var template = await LoadTemplateAsync("classifier-default.md");
        return template
            .Replace("{ISSUE_TITLE}", title)
            .Replace("{ISSUE_BODY}", body)
            .Replace("{CATEGORIES}", categories);
    }

    public static async Task<string> ExtractCasePacketAsync(string issueBody, string comments, string requiredFields)
    {
        var template = await LoadTemplateAsync("extractor-default.md");
        return template
            .Replace("{ISSUE_BODY}", issueBody)
            .Replace("{COMMENTS}", comments)
            .Replace("{REQUIRED_FIELDS}", requiredFields);
    }

    public static async Task<string> GenerateFollowUpQuestionsAsync(string issueBody, string category, List<string> missingFields, List<string> askedBefore)
    {
        // Try category-specific template first
        var template = await LoadTemplateAsync($"followup-{category.ToLowerInvariant()}.md", fallbackToDefault: true);
        return template
            .Replace("{ISSUE_BODY}", issueBody)
            .Replace("{CATEGORY}", category)
            .Replace("{MISSING_FIELDS}", string.Join(", ", missingFields))
            .Replace("{ASKED_BEFORE}", askedBefore.Count > 0 ? $"\n\nFields already asked about (do NOT ask again): {string.Join(", ", askedBefore)}" : string.Empty);
    }

    public static async Task<string> RegenerateFollowUpQuestionsAsync(
        string previousQuestions,
        string judgeRationale,
        string issueBody,
        string category,
        List<string> missingFields,
        List<string> askedBefore)
    {
        var template = await LoadTemplateAsync("followup-regenerate.md");
        return template
            .Replace("{PREVIOUS_QUESTIONS}", previousQuestions)
            .Replace("{JUDGE_FEEDBACK}", judgeRationale)
            .Replace("{ISSUE_BODY}", issueBody)
            .Replace("{CATEGORY}", category)
            .Replace("{MISSING_FIELDS}", string.Join(", ", missingFields))
            .Replace("{ASKED_BEFORE}", askedBefore.Count > 0 ? $"\n\nFields already asked about: {string.Join(", ", askedBefore)}" : string.Empty);
    }

    public static async Task<string> GenerateEngineerBriefAsync(
        string issueBody,
        string comments,
        string category,
        Dictionary<string, string> extractedFields,
        string playbook,
        string repoDocs,
        string duplicatesText)
    {
        var template = await LoadTemplateAsync($"brief-{category.ToLowerInvariant()}.md", fallbackToDefault: true);
        var fieldsText = string.Join("\n", extractedFields.Select(kvp => $"- {kvp.Key}: {kvp.Value}"));

        return template
            .Replace("{ISSUE_BODY}", issueBody)
            .Replace("{COMMENTS}", comments)
            .Replace("{CATEGORY}", category)
            .Replace("{EXTRACTED_FIELDS}", fieldsText)
            .Replace("{PLAYBOOK}", playbook)
            .Replace("{REPO_DOCS}", repoDocs)
            .Replace("{DUPLICATES}", duplicatesText);
    }

    public static async Task<string> RegenerateEngineerBriefAsync(
        string previousBrief,
        string userFeedback,
        Dictionary<string, string> extractedFields,
        string playbook,
        string category)
    {
        var template = await LoadTemplateAsync("brief-regenerate.md");
        var fieldsText = string.Join("\n", extractedFields.Select(kvp => $"- {kvp.Key}: {kvp.Value}"));

        return template
            .Replace("{PREVIOUS_BRIEF}", previousBrief)
            .Replace("{USER_FEEDBACK}", userFeedback)
            .Replace("{EXTRACTED_FIELDS}", fieldsText)
            .Replace("{PLAYBOOK}", playbook)
            .Replace("{CATEGORY}", category);
    }

    public static async Task<string> JudgeFollowUpQuestionsAsync(
        string issueTitle,
        string issueBody,
        string category,
        string playbook,
        List<string> requiredFields,
        List<FollowUpQuestion> questions)
    {
        var template = await LoadTemplateAsync("judge-followup.md");
        var questionsText = string.Join("\n", questions.Select(q => $"- {q.Question}"));

        return template
            .Replace("{ISSUE_TITLE}", issueTitle)
            .Replace("{ISSUE_BODY}", issueBody)
            .Replace("{CATEGORY}", category)
            .Replace("{PLAYBOOK}", playbook)
            .Replace("{REQUIRED_FIELDS}", string.Join(", ", requiredFields))
            .Replace("{QUESTIONS}", questionsText);
    }

    public static async Task<string> JudgeEngineerBriefAsync(
        string issueTitle,
        string issueBody,
        string category,
        string playbook,
        List<string> requiredFields,
        EngineerBrief brief)
    {
        var template = await LoadTemplateAsync("judge-brief.md");
        var evidenceText = string.Join("\n", brief.KeyEvidence.Select(e => $"- {e}"));

        return template
            .Replace("{ISSUE_TITLE}", issueTitle)
            .Replace("{ISSUE_BODY}", issueBody)
            .Replace("{CATEGORY}", category)
            .Replace("{PLAYBOOK}", playbook)
            .Replace("{REQUIRED_FIELDS}", string.Join(", ", requiredFields))
            .Replace("{BRIEF_SUMMARY}", brief.Summary)
            .Replace("{KEY_EVIDENCE}", evidenceText);
    }

    private static async Task<string> LoadTemplateAsync(string templateName, bool fallbackToDefault = false)
    {
        var path = Path.Combine(PromptsDirectory, "templates", templateName);

        if (File.Exists(path))
        {
            return await File.ReadAllTextAsync(path);
        }

        if (fallbackToDefault && templateName.Contains("-"))
        {
            // Try loading default version: followup-bug.md -> followup-default.md
            var parts = templateName.Split('-');
            var defaultName = $"{parts[0]}-default.{parts[^1]}";
            var defaultPath = Path.Combine(PromptsDirectory, "templates", defaultName);

            if (File.Exists(defaultPath))
            {
                return await File.ReadAllTextAsync(defaultPath);
            }
        }

        throw new FileNotFoundException($"Prompt template not found: {templateName} (looked in {path})");
    }

    private static string GetPromptsDirectory()
    {
        // First check if prompts directory exists in workspace root
        var workspaceRoot = FindWorkspaceRoot();
        var promptsDir = Path.Combine(workspaceRoot, "prompts");

        if (Directory.Exists(promptsDir))
        {
            return promptsDir;
        }

        // Fallback: look for it relative to assembly location
        var assemblyDir = Path.GetDirectoryName(typeof(Prompts).Assembly.Location) ?? ".";
        var fallbackDir = Path.Combine(assemblyDir, "..", "..", "..", "prompts");

        return fallbackDir;
    }

    private static string FindWorkspaceRoot()
    {
        var current = Directory.GetCurrentDirectory();

        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "SupportConcierge.slnx")) ||
                Directory.Exists(Path.Combine(current, ".supportbot")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
