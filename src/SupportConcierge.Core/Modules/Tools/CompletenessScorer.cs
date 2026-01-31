using SupportConcierge.Core.Modules.Guardrails;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.SpecPack;

namespace SupportConcierge.Core.Modules.Tools;

public sealed class CompletenessScorer
{
    private readonly Validators _validators;

    public CompletenessScorer(Validators validators)
    {
        _validators = validators;
    }

    public ScoringResult Score(Dictionary<string, string> extractedFields, CategoryChecklist checklist)
    {
        var result = new ScoringResult
        {
            Category = checklist.Category,
            Threshold = checklist.CompletenessThreshold
        };

        var totalWeight = 0;
        var earnedWeight = 0;

        foreach (var requiredField in checklist.RequiredFields)
        {
            totalWeight += requiredField.Weight;

            var fieldValue = FindFieldValue(extractedFields, requiredField);
            if (fieldValue == null)
            {
                result.MissingFields.Add(requiredField.Name);
                if (!requiredField.Optional)
                {
                    result.Issues.Add($"Required field '{requiredField.Name}' is missing");
                }
                continue;
            }

            var validation = _validators.ValidateField(requiredField.Name, fieldValue);
            if (!validation.IsValid)
            {
                result.InvalidFields.Add(requiredField.Name);
                result.Issues.Add($"Field '{requiredField.Name}': {validation.Message}");
                if (!requiredField.Optional)
                {
                    earnedWeight += requiredField.Weight / 3;
                }
            }
            else
            {
                earnedWeight += requiredField.Weight;
            }
        }

        result.Score = totalWeight > 0 ? (int)Math.Round((double)earnedWeight / totalWeight * 100) : 0;
        result.IsActionable = result.Score >= checklist.CompletenessThreshold;
        result.Warnings.AddRange(_validators.CheckContradictions(extractedFields));

        return result;
    }

    private static string? FindFieldValue(Dictionary<string, string> fields, RequiredField requiredField)
    {
        if (fields.TryGetValue(requiredField.Name, out var value))
        {
            return value;
        }

        foreach (var alias in requiredField.Aliases)
        {
            if (fields.TryGetValue(alias, out value))
            {
                return value;
            }
        }

        return null;
    }
}

