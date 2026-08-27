using System.Globalization;
using System.Text.Json;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal sealed record ComplexityBudget(double PropagationCost, int CoreSize)
{
    public const string FileName = ".harness.budget.json";

    public const double NoticeablePropagationDelta = 0.01;

    public static ComplexityBudget From(RepositoryComplexity metric)
        => new(Math.Round(metric.PropagationCostPercentage, 6, MidpointRounding.AwayFromZero), metric.CoreFiles);

    public static (ComplexityBudget? Budget, string? Failure) Load(
        CheckContext context,
        EvidenceFile evidence)
    {
        var entry = context.Tracked(evidence).FirstOrDefault(candidate => candidate.Path == FileName);
        if (entry is null)
        {
            return (null, $"'{FileName}' is not tracked; run `harness budget update`, review it, and add it to Git.");
        }

        var (text, failure) = context.Repository.ReadTrackedText(entry);
        return text is null ? (null, failure) : Parse(text);
    }

    public static (ComplexityBudget? Budget, string? Failure) LoadWorking(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : (null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not read '{FileName}': {exception.Message}");
        }
    }

    public string Serialize()
        => "{\n"
            + "  \"complexity.csharp\": {\n"
            + $"    \"propagationCost\": {PropagationCost.ToString("0.######", CultureInfo.InvariantCulture)},\n"
            + $"    \"coreSize\": {CoreSize.ToString(CultureInfo.InvariantCulture)}\n"
            + "  }\n"
            + "}\n";

    private static (ComplexityBudget? Budget, string? Failure) Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("complexity.csharp", out var complexity)
                || complexity.ValueKind != JsonValueKind.Object
                || complexity.EnumerateObject().Count() != 2
                || !complexity.TryGetProperty("propagationCost", out var propagation)
                || !propagation.TryGetDouble(out var propagationCost)
                || !double.IsFinite(propagationCost)
                || propagationCost is < 0 or > 100
                || !complexity.TryGetProperty("coreSize", out var core)
                || !core.TryGetInt32(out var coreSize)
                || coreSize < 0)
            {
                return (null, InvalidShape);
            }

            return (new ComplexityBudget(propagationCost, coreSize), null);
        }
        catch (JsonException exception)
        {
            return (null, $"'{FileName}' is not readable as JSON ({exception.Message}).");
        }
    }

    private static string InvalidShape
        => $"'{FileName}' must contain only complexity.csharp with numeric propagationCost (0..100) and integer coreSize (>= 0).";
}
