namespace SupportConcierge.Core.Modules.Guardrails;

public static class DisagreementDetector
{
    private static readonly string[] Keywords =
    {
        "doesn't apply", "dont apply", "does not apply", "do not apply",
        "already tried", "already did", "already done",
        "didn't work", "did not work", "doesn't work", "does not work",
        "still broken", "still failing", "still see", "still getting",
        "not working", "not relevant", "not applicable",
        "different error", "different issue", "different problem",
        "need clarification", "not sure how", "unclear how",
        "not my case", "not my situation", "doesn't match",
        "disagree", "disagrees", "disagreed", "disagreement"
    };

    public static bool IsDisagreement(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return false;
        }

        var lower = comment.ToLowerInvariant();
        return Keywords.Any(keyword => lower.Contains(keyword));
    }
}

