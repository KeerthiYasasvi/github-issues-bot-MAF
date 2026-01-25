namespace SupportConcierge.Guardrails;

public static class CommandParser
{
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
