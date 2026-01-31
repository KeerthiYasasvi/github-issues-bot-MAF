using System.Text;

namespace SupportConcierge.Core.Modules.Prompts;

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
            var error = $"MAF prompt template not found: {templateName} (looked in {path})";
            Console.WriteLine($"[Prompts] ERROR: {error}");
            throw new FileNotFoundException(error);
        }

        Console.WriteLine($"[Prompts] Loading template: {templateName}");
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static string ResolvePromptsDirectory()
    {
        var workspaceRoot = FindWorkspaceRoot();
        var promptsDir = Path.Combine(workspaceRoot, "prompts");
        Console.WriteLine($"[Prompts] Checking workspace root prompts: {promptsDir}");
        if (Directory.Exists(promptsDir))
        {
            Console.WriteLine($"[Prompts] ✓ Found prompts directory: {promptsDir}");
            return promptsDir;
        }
        Console.WriteLine($"[Prompts] ✗ workspace root prompts not found");

        // Try relative to bot subdirectory (used in test repos)
        var botPromptsDir = Path.Combine(workspaceRoot, "bot", "prompts");
        Console.WriteLine($"[Prompts] Checking bot/prompts: {botPromptsDir}");
        if (Directory.Exists(botPromptsDir))
        {
            Console.WriteLine($"[Prompts] ✓ Found bot/prompts directory: {botPromptsDir}");
            return botPromptsDir;
        }
        Console.WriteLine($"[Prompts] ✗ bot/prompts not found (exists bot dir? {Directory.Exists(Path.Combine(workspaceRoot, "bot"))})");

        // Try parent directories (in case we're in a bin subdirectory)
        Console.WriteLine($"[Prompts] Searching parent directories from: {workspaceRoot}");
        var current = workspaceRoot;
        for (int i = 0; i < 6; i++)
        {
            var parentPromptsDir = Path.Combine(current, "prompts");
            Console.WriteLine($"[Prompts] Parent search level {i}: checking {parentPromptsDir}");
            if (Directory.Exists(parentPromptsDir))
            {
                Console.WriteLine($"[Prompts] ✓ Found parent prompts directory: {parentPromptsDir}");
                return parentPromptsDir;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent == null || parent == current)
            {
                Console.WriteLine($"[Prompts] Reached filesystem root");
                break;
            }
            current = parent;
        }

        var assemblyDir = Path.GetDirectoryName(typeof(MafPromptTemplates).Assembly.Location) ?? ".";
        var fallbackDir = Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "..", "prompts");
        Console.WriteLine($"[Prompts] Using fallback directory: {fallbackDir}");
        return fallbackDir;
    }

    private static string FindWorkspaceRoot()
    {
        var current = Directory.GetCurrentDirectory();
        Console.WriteLine($"[Prompts] Starting workspace search from: {current}");
        var searchedPaths = new List<string>();
        
        while (current != null)
        {
            searchedPaths.Add(current);
            
            // Check for solution file
            if (File.Exists(Path.Combine(current, "SupportConcierge.slnx")))
            {
                Console.WriteLine($"[Prompts] Found workspace root (slnx): {current}");
                return current;
            }
            
            // Check for .supportbot
            if (Directory.Exists(Path.Combine(current, ".supportbot")))
            {
                Console.WriteLine($"[Prompts] Found workspace root (.supportbot): {current}");
                return current;
            }
            
            // Check for bot subdirectory (test repo setup)
            if (Directory.Exists(Path.Combine(current, "bot")) &&
                File.Exists(Path.Combine(current, "bot", "SupportConcierge.slnx")))
            {
                Console.WriteLine($"[Prompts] Found bot workspace root: {current}");
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        Console.WriteLine($"[Prompts] No workspace root found, searched: {string.Join(", ", searchedPaths.Take(5))}...");
        return Directory.GetCurrentDirectory();
    }
}

