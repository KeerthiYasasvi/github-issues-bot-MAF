using System.Text.Json;
using Json.Schema;

namespace SupportConcierge.Core.Tools;

public sealed class SchemaValidator
{
    public bool TryValidate(string json, string schemaJson, out List<string> errors)
    {
        errors = new List<string>();
        JsonDocument? document = null;
        JsonSchema? schema = null;

        try
        {
            schema = JsonSchema.FromText(schemaJson);
        }
        catch (Exception ex)
        {
            errors.Add($"Schema parse error: {ex.Message}");
            return false;
        }

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            errors.Add($"JSON parse error: {ex.Message}");
            return false;
        }

        var result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
        if (result.IsValid)
        {
            return true;
        }

        CollectErrors(result, errors);
        return false;
    }

    private static void CollectErrors(EvaluationResults results, List<string> errors)
    {
        if (!results.IsValid && results.Errors != null)
        {
            foreach (var entry in results.Errors)
            {
                errors.Add(entry.Value);
            }
        }

        if (results.Details != null)
        {
            foreach (var child in results.Details)
            {
                CollectErrors(child, errors);
            }
        }
    }
}
