using SupportConcierge.Core.SpecPack;

namespace SupportConcierge.Core.Tools;

public sealed class CategoryScorer
{
    public (string? Category, Dictionary<string, int> Scores) Score(string title, string body, List<Category> categories)
    {
        var text = $"{title} {body}".ToLowerInvariant();
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            var score = category.Keywords.Count(keyword => text.Contains(keyword.ToLowerInvariant()));
            scores[category.Name] = score;
        }

        var best = scores.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        if (best.Value > 0)
        {
            return (best.Key, scores);
        }

        return (null, scores);
    }

    public bool ShouldRouteOffTopic(Dictionary<string, int> scores, string text)
    {
        var lower = text.ToLowerInvariant();
        var problemPatterns = new[]
        {
            "error message", "exception", "fail", "failed", "failure", "crash", "crashed", "stack trace",
            "not working", "doesn't work", "doesnt work", "unable to", "cannot", "can't", "can not",
            "won't", "wont", "bug", "regression", "broken", "issue with", "problem with"
        };
        var negationPatterns = new[] { "no error", "no errors", "no exception", "no crash", "no fail" };

        var hasProblemTerms = problemPatterns.Any(term => lower.Contains(term));
        var hasNegation = negationPatterns.Any(term => lower.Contains(term));

        return scores.TryGetValue("off_topic", out var offTopicScore) && offTopicScore > 0 && (!hasProblemTerms || hasNegation);
    }
}
