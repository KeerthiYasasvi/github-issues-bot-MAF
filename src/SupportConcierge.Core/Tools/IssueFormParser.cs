using System.Text.RegularExpressions;

namespace SupportConcierge.Core.Tools;

public sealed class IssueFormParser
{
    public Dictionary<string, string> ParseIssueForm(string issueBody)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(issueBody))
        {
            return fields;
        }

        var lines = issueBody.Split('\n');
        string? currentField = null;
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            var headingMatch = Regex.Match(line, "^#+\\s+(.+)$");
            if (headingMatch.Success)
            {
                if (currentField != null)
                {
                    var content = string.Join("\n", currentContent).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        fields[NormalizeFieldName(currentField)] = content;
                    }
                }

                currentField = headingMatch.Groups[1].Value.Trim();
                currentContent.Clear();
                continue;
            }

            if (currentField != null)
            {
                currentContent.Add(line);
            }
        }

        if (currentField != null)
        {
            var content = string.Join("\n", currentContent).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                fields[NormalizeFieldName(currentField)] = content;
            }
        }

        return fields;
    }

    public Dictionary<string, string> ExtractKeyValuePairs(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fields;
        }

        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            var match = Regex.Match(line, "^\\s*([^:=]+)\\s*[:=]\\s*(.+)$");
            if (!match.Success)
            {
                continue;
            }

            var key = NormalizeFieldName(match.Groups[1].Value.Trim());
            var value = match.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields[key] = value;
            }
        }

        return fields;
    }

    public Dictionary<string, string> MergeFields(params Dictionary<string, string>[] fieldDicts)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in fieldDicts)
        {
            foreach (var kvp in dict)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        return merged;
    }

    private static string NormalizeFieldName(string fieldName)
    {
        var normalized = Regex.Replace(fieldName, "[^\\w\\s]", string.Empty);
        normalized = Regex.Replace(normalized, "\\s+", "_");
        return normalized.ToLowerInvariant();
    }
}
