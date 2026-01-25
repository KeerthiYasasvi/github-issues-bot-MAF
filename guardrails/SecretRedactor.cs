using System.Text.RegularExpressions;

namespace SupportConcierge.Guardrails;

public sealed class SecretRedactor
{
    private readonly List<Regex> _secretPatterns;
    private const string RedactionPlaceholder = "[REDACTED]";

    public SecretRedactor(IEnumerable<string> secretPatternStrings)
    {
        _secretPatterns = secretPatternStrings
            .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            .ToList();
    }

    public (string RedactedText, List<string> Findings) Redact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (text ?? string.Empty, new List<string>());
        }

        var findings = new List<string>();
        var redactedText = text;

        foreach (var pattern in _secretPatterns)
        {
            var matches = pattern.Matches(redactedText);
            foreach (Match match in matches)
            {
                if (!match.Success || string.IsNullOrWhiteSpace(match.Value))
                {
                    continue;
                }

                var secretType = DetermineSecretType(match.Value);
                findings.Add($"Found {secretType}: {match.Value.Substring(0, Math.Min(12, match.Value.Length))}...");
                redactedText = redactedText.Replace(match.Value, RedactionPlaceholder);
            }
        }

        return (redactedText, findings);
    }

    private static string DetermineSecretType(string secret)
    {
        var lower = secret.ToLowerInvariant();
        if (lower.Contains("api") || lower.Contains("key") || lower.Contains("token"))
        {
            return "API Key";
        }
        if (lower.Contains("password") || lower.Contains("passwd") || lower.Contains("pwd"))
        {
            return "Password";
        }
        if (lower.Contains("secret"))
        {
            return "Secret";
        }
        if (lower.Contains("credential"))
        {
            return "Credential";
        }
        if (lower.Contains("bearer"))
        {
            return "Bearer Token";
        }

        return "Sensitive Data";
    }
}
