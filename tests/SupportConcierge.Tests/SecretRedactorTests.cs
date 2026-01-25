using SupportConcierge.Core.Guardrails;
using Xunit;

namespace SupportConcierge.Tests;

public class SecretRedactorTests
{
    [Fact]
    public void Redact_RemovesSecrets()
    {
        var patterns = new List<string> { "(?i)(api[_\\-]?key)\\s*[:=]\\s*['\"]?([a-zA-Z0-9_\\-]{20,})['\"]?" };
        var redactor = new SecretRedactor(patterns);

        var input = "api_key=abcdefghijklmnopqrstuvwx";
        var (redacted, findings) = redactor.Redact(input);

        Assert.Contains("[REDACTED]", redacted);
        Assert.NotEmpty(findings);
    }
}
