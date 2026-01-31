namespace SupportConcierge.Core.Modules.Guardrails;

using SupportConcierge.Core.Modules.Models;

public static class CommandParser
{
    /// <summary>
    /// Parses text for special bot commands (/stop, /diagnose) and returns a CommandInfo object.
    /// </summary>
    /// <param name="text">The text to parse for commands</param>
    /// <returns>A CommandInfo object with detected command flags</returns>
    public static CommandInfo Parse(string? text)
    {
        return new CommandInfo
        {
            HasStopCommand = HasStopCommand(text),
            HasDiagnoseCommand = HasDiagnoseCommand(text)
        };
    }

    public static bool HasStopCommand(string? text)
    {
        return ContainsCommand(text, "/stop");
    }

    public static bool HasDiagnoseCommand(string? text)
    {
        return ContainsCommand(text, "/diagnose");
    }

    private static bool ContainsCommand(string? text, string command)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.IndexOf(command, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}


