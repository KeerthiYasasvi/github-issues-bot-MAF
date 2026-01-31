using System.Text.RegularExpressions;
using SupportConcierge.Core.Modules.SpecPack;

namespace SupportConcierge.Core.Modules.Guardrails;

public sealed class Validators
{
    private readonly ValidatorRules _rules;
    private readonly List<Regex> _junkPatterns;
    private readonly Dictionary<string, Regex> _formatValidators;

    public Validators(ValidatorRules rules)
    {
        _rules = rules;
        _junkPatterns = rules.JunkPatterns
            .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            .ToList();
        _formatValidators = rules.FormatValidators
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new Regex(kvp.Value, RegexOptions.Compiled),
                StringComparer.OrdinalIgnoreCase);
    }

    public ValidationResult ValidateField(string fieldName, string? value)
    {
        var result = new ValidationResult { FieldName = fieldName, IsValid = true };

        if (string.IsNullOrWhiteSpace(value))
        {
            result.IsValid = false;
            result.Message = "Field is empty";
            return result;
        }

        if (IsJunkValue(value))
        {
            result.IsValid = false;
            result.Message = "Field contains placeholder or junk value";
            return result;
        }

        var normalizedFieldName = fieldName.ToLowerInvariant();
        foreach (var validator in _formatValidators)
        {
            if (normalizedFieldName.Contains(validator.Key.ToLowerInvariant()) && !validator.Value.IsMatch(value))
            {
                result.IsValid = false;
                result.Message = $"Field does not match expected format for {validator.Key}";
                return result;
            }
        }

        return result;
    }

    public bool IsJunkValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return _junkPatterns.Any(pattern => pattern.IsMatch(value.Trim()));
    }

    public List<string> CheckContradictions(Dictionary<string, string> fields)
    {
        var warnings = new List<string>();

        foreach (var rule in _rules.ContradictionRules)
        {
            if (!fields.TryGetValue(rule.Field1, out var value1) || !fields.TryGetValue(rule.Field2, out var value2))
            {
                continue;
            }

            var lower1 = value1.ToLowerInvariant();
            var lower2 = value2.ToLowerInvariant();

            switch (rule.Condition.ToLowerInvariant())
            {
                case "version_mismatch":
                    var v1 = ExtractVersionNumber(lower1);
                    var v2 = ExtractVersionNumber(lower2);
                    if (v1.HasValue && v2.HasValue && Math.Abs(v1.Value - v2.Value) > 2)
                    {
                        warnings.Add($"{rule.Description}: {rule.Field1} ({value1}) may be incompatible with {rule.Field2} ({value2})");
                    }
                    break;
                case "windows_with_bash_native":
                    if (lower1.Contains("windows") && lower2.Contains("bash") && !lower2.Contains("wsl"))
                    {
                        warnings.Add($"{rule.Description}: Windows typically requires WSL for bash");
                    }
                    break;
            }
        }

        return warnings;
    }

    private static int? ExtractVersionNumber(string text)
    {
        var match = Regex.Match(text, @"(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
        {
            return version;
        }

        return null;
    }
}

public sealed class ValidationResult
{
    public string FieldName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
}

