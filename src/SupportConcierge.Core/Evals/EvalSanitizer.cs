using System.Security.Cryptography;
using System.Text;
using SupportConcierge.Core.Guardrails;

namespace SupportConcierge.Core.Evals;

public static class EvalSanitizer
{
    public static (string Hash, string Snippet, bool Redacted) HashAndSnippet(string text, int maxLen = 300)
    {
        var input = text ?? string.Empty;
        var redactor = new SecretRedactor(Array.Empty<string>());
        var (redacted, findings) = redactor.Redact(input);
        var snippet = redacted.Length > maxLen ? redacted.Substring(0, maxLen) : redacted;
        return (HashString(input), snippet, findings.Count > 0);
    }

    public static string HashString(string text)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
