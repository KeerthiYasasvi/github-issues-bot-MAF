namespace SupportConcierge.Core.Modules.Models;

/// <summary>
/// Represents the result of parsing text for special bot commands.
/// </summary>
public record CommandInfo
{
    /// <summary>
    /// Indicates if the /stop command was detected in the text.
    /// The /stop command tells the bot to stop asking questions and finalize the issue.
    /// </summary>
    public bool HasStopCommand { get; set; }

    /// <summary>
    /// Indicates if the /diagnose command was detected in the text.
    /// The /diagnose command tells the bot to reset state and start fresh analysis.
    /// </summary>
    public bool HasDiagnoseCommand { get; set; }
}

