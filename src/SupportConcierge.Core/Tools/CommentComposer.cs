using System.Text;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Tools;

public sealed class CommentComposer
{
    public string ComposeFollowUpComment(List<FollowUpQuestion> questions, int loopCount, string? username)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(username))
        {
            sb.AppendLine($"@{username}");
            sb.AppendLine();
        }

        sb.AppendLine("Hi! I need a bit more information to help route this issue effectively.");
        sb.AppendLine();

        for (var i = 0; i < questions.Count; i++)
        {
            sb.AppendLine($"**{i + 1}. {questions[i].Question}**");
            sb.AppendLine($"   _{questions[i].WhyNeeded}_");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"_This is follow-up round {loopCount} of 3. Please provide as much detail as possible._");
        sb.AppendLine();
        sb.AppendLine("### Quick Commands");
        sb.AppendLine("- **`/stop`** - Stop asking me questions on this issue (opt-out)");
        sb.AppendLine("- **`/diagnose`** - Activate the bot for your specific sub-issue or different problem (for other users in this thread)");

        return sb.ToString();
    }

    public string ComposeEngineerBrief(EngineerBrief brief, ScoringResult scoring, Dictionary<string, string> extractedFields, List<string> secretWarnings, string? username)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(username))
        {
            sb.AppendLine($"@{username}");
            sb.AppendLine();
        }

        sb.AppendLine($"**Summary:** {brief.Summary}");
        sb.AppendLine();

        if (brief.Symptoms.Count > 0)
        {
            sb.AppendLine("### Symptoms");
            foreach (var symptom in brief.Symptoms)
            {
                sb.AppendLine($"- {symptom}");
            }
            sb.AppendLine();
        }

        if (brief.Environment.Count > 0)
        {
            sb.AppendLine("### Environment");
            foreach (var kvp in brief.Environment)
            {
                sb.AppendLine($"- **{kvp.Key}:** {kvp.Value}");
            }
            sb.AppendLine();
        }

        if (brief.ReproSteps.Count > 0)
        {
            sb.AppendLine("### Reproduction Steps");
            for (var i = 0; i < brief.ReproSteps.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {brief.ReproSteps[i]}");
            }
            sb.AppendLine();
        }

        if (brief.KeyEvidence.Count > 0)
        {
            sb.AppendLine("### Key Evidence");
            sb.AppendLine("```");
            foreach (var evidence in brief.KeyEvidence)
            {
                sb.AppendLine(evidence);
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (scoring.Warnings.Count > 0 || secretWarnings.Count > 0)
        {
            sb.AppendLine("### Warnings");
            foreach (var warning in scoring.Warnings)
            {
                sb.AppendLine($"- {warning}");
            }
            foreach (var warning in secretWarnings)
            {
                sb.AppendLine($"- {warning}");
            }
            sb.AppendLine();
        }

        if (brief.NextSteps.Count > 0)
        {
            sb.AppendLine("### Suggested Next Steps");
            foreach (var step in brief.NextSteps)
            {
                sb.AppendLine($"- {step}");
            }
            sb.AppendLine();
        }

        if (brief.ValidationConfirmations.Count > 0)
        {
            sb.AppendLine("### Please Confirm");
            sb.AppendLine("Before proceeding with the steps above, please confirm:");
            foreach (var confirmation in brief.ValidationConfirmations)
            {
                sb.AppendLine($"- {confirmation}");
            }
            sb.AppendLine();
        }

        if (brief.PossibleDuplicates.Count > 0)
        {
            sb.AppendLine("### Possibly Related Issues");
            foreach (var dup in brief.PossibleDuplicates)
            {
                sb.AppendLine($"- #{dup.IssueNumber}: {dup.SimilarityReason}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### Quick Commands");
        sb.AppendLine("- **`/stop`** - Stop asking me questions on this issue (opt-out)");
        sb.AppendLine("- **`/diagnose`** - Activate the bot for your specific sub-issue or different problem (for other users in this thread)");
        sb.AppendLine();
        sb.AppendLine("If this brief does not fit, reply with 'I disagree' or similar and I'll re-iterate once before escalating.");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>Case Packet (JSON)</summary>");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(extractedFields, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("```");
        sb.AppendLine("</details>");
        sb.AppendLine();
        sb.AppendLine($"**Completeness Score:** {scoring.Score}/100 (threshold: {scoring.Threshold})");

        return sb.ToString();
    }

    public string ComposeOffTopicComment(string? username)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(username))
        {
            sb.AppendLine($"@{username}");
            sb.AppendLine();
        }

        sb.AppendLine("Thanks for reaching out! This tracker is for bugs and feature requests. This looks more like a how-to/support question (e.g., build/run/clone/installation).");
        sb.AppendLine();
        sb.AppendLine("Please check the project docs for setup and usage:");
        sb.AppendLine("- README (setup/build/run)");
        sb.AppendLine("- CONTRIBUTING (environment and workflow)");
        sb.AppendLine();
        sb.AppendLine("If you believe this is a repository issue, please reply with:");
        sb.AppendLine("- The exact error message");
        sb.AppendLine("- Steps to reproduce");
        sb.AppendLine("- Your environment (OS, versions)");

        return sb.ToString();
    }

    public string ComposeEscalationComment(ScoringResult scoring, List<string> escalationMentions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Escalation Notice");
        sb.AppendLine();
        sb.AppendLine("After 3 rounds of follow-up questions, this issue still does not have enough information to be actionable.");
        sb.AppendLine();
        sb.AppendLine("### Still Missing");
        foreach (var field in scoring.MissingFields)
        {
            sb.AppendLine($"- {field}");
        }
        sb.AppendLine();

        if (scoring.Issues.Count > 0)
        {
            sb.AppendLine("### Issues Identified");
            foreach (var issue in scoring.Issues)
            {
                sb.AppendLine($"- {issue}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"**Current Completeness Score:** {scoring.Score}/100 (needs {scoring.Threshold})");
        sb.AppendLine();
        var mentions = string.Join(" ", escalationMentions);
        sb.AppendLine($"Tagging for manual review: {mentions}");

        return sb.ToString();
    }

    public string ComposeStopAcknowledgement(string? username)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(username))
        {
            sb.AppendLine($"@{username}");
            sb.AppendLine();
        }

        sb.AppendLine("You've opted out with /stop. I won't ask further questions on this issue. If you need to restart, comment with /diagnose.");
        return sb.ToString();
    }
}
