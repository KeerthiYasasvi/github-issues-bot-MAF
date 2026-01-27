using System.Text.RegularExpressions;

namespace SupportConcierge.Core.Guardrails;

/// <summary>
/// Detects and redacts sensitive information including API keys, passwords,
/// tokens, Base64-encoded data, and cryptographic hashes.
/// </summary>
public sealed class SecretRedactor
{
    private readonly List<Regex> _secretPatterns;
    private const string RedactionPlaceholder = "[REDACTED]";
    
    // Pattern for Base64 strings (20+ characters of valid Base64 characters)
    private static readonly Regex Base64Pattern = new(
        @"[A-Za-z0-9+/]{20,}={0,2}(?:\s|$)",
        RegexOptions.Compiled
    );
    
    // Pattern for cryptographic hashes and tokens (32+ hex characters or high-entropy strings)
    private static readonly Regex HashTokenPattern = new(
        @"\b[a-fA-F0-9]{32,}\b|\b[A-Za-z0-9_-]{40,}\b",
        RegexOptions.Compiled
    );

    public SecretRedactor(IEnumerable<string> secretPatternStrings)
    {
        _secretPatterns = secretPatternStrings
            .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Redacts all discovered secrets from text.
    /// Returns tuple of (redacted text, list of findings).
    /// Preview length increased to 20 characters for better context.
    /// </summary>
    public (string RedactedText, List<string> Findings) Redact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (text ?? string.Empty, new List<string>());
        }

        var findings = new List<string>();
        var redactedText = text;

        // Check configured patterns first (from SpecPack)
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
                var preview = GetSecretPreview(match.Value, 20);
                findings.Add($"Found {secretType}: {preview}...");
                redactedText = redactedText.Replace(match.Value, RedactionPlaceholder);
            }
        }

        // Check for Base64-encoded secrets
        var base64Matches = Base64Pattern.Matches(redactedText);
        foreach (Match match in base64Matches)
        {
            if (!match.Success || string.IsNullOrWhiteSpace(match.Value))
            {
                continue;
            }

            // Only redact if it looks like a valid Base64 secret (not common English)
            if (IsLikelyBase64Secret(match.Value.Trim()))
            {
                var preview = GetSecretPreview(match.Value, 20);
                findings.Add($"Found Base64 Encoded Data: {preview}...");
                redactedText = redactedText.Replace(match.Value, RedactionPlaceholder);
            }
        }

        // Check for hash and token patterns
        var hashMatches = HashTokenPattern.Matches(redactedText);
        foreach (Match match in hashMatches)
        {
            if (!match.Success || string.IsNullOrWhiteSpace(match.Value))
            {
                continue;
            }

            // Avoid matching common words or short strings
            if (match.Value.Length >= 32 && !IsCommonWord(match.Value))
            {
                var preview = GetSecretPreview(match.Value, 20);
                findings.Add($"Found Hash/Token: {preview}...");
                redactedText = redactedText.Replace(match.Value, RedactionPlaceholder);
            }
        }

        return (redactedText, findings);
    }

    /// <summary>
    /// Determines the type of secret for better logging and diagnostics.
    /// Enhanced with Base64 and hash/token detection.
    /// </summary>
    private static string DetermineSecretType(string secret)
    {
        var lower = secret.ToLowerInvariant();

        // Check for specific secret types in order of specificity
        if (lower.Contains("api") && (lower.Contains("key") || lower.Contains("token")))
        {
            return "API Key";
        }
        if (lower.Contains("bearer") && lower.Contains("token"))
        {
            return "Bearer Token";
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
        if (lower.Contains("token"))
        {
            return "Token";
        }
        if (lower.Contains("key"))
        {
            return "Encryption Key";
        }

        return "Sensitive Data";
    }

    /// <summary>
    /// Extracts a preview of the secret (first N characters) for logging.
    /// </summary>
    private static string GetSecretPreview(string secret, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return "[empty]";
        }

        var trimmed = secret.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed.Substring(0, maxLength);
    }

    /// <summary>
    /// Checks if a string is likely Base64-encoded data (not just random Base64-like text).
    /// Uses entropy and length heuristics.
    /// </summary>
    private static bool IsLikelyBase64Secret(string value)
    {
        if (value.Length < 20)
        {
            return false;
        }

        // Check for padding and valid Base64 structure
        var validBase64 = Regex.IsMatch(value, @"^[A-Za-z0-9+/]{4}*={0,2}$");
        if (!validBase64)
        {
            return false;
        }

        // Simple entropy check - count unique characters
        var uniqueChars = value.Distinct().Count();
        return uniqueChars >= 30; // High entropy = likely encoded data, not text
    }

    /// <summary>
    /// Checks if a string is a common English word to avoid false positives.
    /// </summary>
    private static bool IsCommonWord(string value)
    {
        // List of common words that might match hash patterns
        var commonWords = new[]
        {
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Common test strings
            "0000000000000000000000000000000",
            "ffffffffffffffffffffffffffffffff",
            "example", "placeholder", "test", "sample",
        };

        return commonWords.Contains(value.ToLowerInvariant());
    }
}
