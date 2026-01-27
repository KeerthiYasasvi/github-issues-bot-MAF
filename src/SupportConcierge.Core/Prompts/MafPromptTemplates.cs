using System.Text;

namespace SupportConcierge.Core.Prompts;

public static class MafPromptTemplates
{
    private const string SystemMarker = "===SYSTEM===";
    private const string UserMarker = "===USER===";
    private static readonly string PromptsDirectory = ResolvePromptsDirectory();

    public static async Task<(string System, string User)> LoadAsync(
        string templateName,
        Dictionary<string, string> tokens,
        CancellationToken cancellationToken = default)
    {
        var template = await LoadTemplateAsync(templateName, cancellationToken);
        var (system, user) = SplitTemplate(templateName, template);
        system = ReplaceTokens(system, tokens);
        user = ReplaceTokens(user, tokens);
        return (system, user);
    }

    private static string ReplaceTokens(string template, Dictionary<string, string> tokens)
    {
        var result = new StringBuilder(template);
        foreach (var kvp in tokens)
        {
            result.Replace("{" + kvp.Key + "}", kvp.Value ?? string.Empty);
        }

        return result.ToString();
    }

    private static (string System, string User) SplitTemplate(string templateName, string content)
    {
        var systemIndex = content.IndexOf(SystemMarker, StringComparison.Ordinal);
        var userIndex = content.IndexOf(UserMarker, StringComparison.Ordinal);
        if (systemIndex < 0 || userIndex < 0 || userIndex <= systemIndex)
        {
            throw new InvalidOperationException($"Prompt template '{templateName}' must include {SystemMarker} and {UserMarker} sections.");
        }

        var systemStart = systemIndex + SystemMarker.Length;
        var system = content.Substring(systemStart, userIndex - systemStart).Trim();
        var userStart = userIndex + UserMarker.Length;
        var user = content.Substring(userStart).Trim();

        return (system, user);
    }

    private static async Task<string> LoadTemplateAsync(string templateName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(PromptsDirectory, "maf-templates", templateName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"MAF prompt template not found: {templateName} (looked in {path})");
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static string ResolvePromptsDirectory()
    {
        var workspaceRoot = FindWorkspaceRoot();
        var promptsDir = Path.Combine(workspaceRoot, "prompts");
        if (Directory.Exists(promptsDir))
        {
            return promptsDir;
        }

        var assemblyDir = Path.GetDirectoryName(typeof(MafPromptTemplates).Assembly.Location) ?? ".";
        var fallbackDir = Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "..", "prompts");
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
