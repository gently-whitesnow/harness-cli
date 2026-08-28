using System.Globalization;
using System.Text;
using System.Text.Json;
using Harness.Contracts.Files;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal sealed record ComplexityBudget(IReadOnlyDictionary<string, ComplexityBudget.Entry> Entries)
{
    public const string FileName = ".harness.budget.json";

    public const double NoticeablePropagationDelta = 0.01;

    public static (ComplexityBudget? Budget, string? Failure) Load(
        CheckContext context,
        EvidenceFile evidence,
        IReadOnlyList<string> expectedIds)
    {
        var entry = context.Tracked(evidence).FirstOrDefault(candidate => candidate.Path == FileName);
        if (entry is null)
        {
            return (null, $"'{FileName}' is not tracked; run `harness budget update`, review it, and add it to Git.");
        }

        var (text, failure) = context.Repository.ReadTrackedText(entry);
        return text is null ? (null, failure) : Parse(text, expectedIds);
    }

    public static (ComplexityBudget? Budget, string? Failure) LoadWorking(
        IFileSystem files,
        string path,
        IReadOnlyList<string> expectedIds)
    {
        try
        {
            return files.Exists(path) ? Parse(files.ReadText(path), expectedIds) : (null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not read '{FileName}': {exception.Message}");
        }
    }

    public string Serialize()
    {
        var text = new StringBuilder("{\n");
        var entries = Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList();
        for (var index = 0; index < entries.Count; index++)
        {
            var (id, value) = entries[index];
            text.Append("  \"").Append(id).Append("\": {\n")
                .Append("    \"propagationCost\": ")
                .Append(value.PropagationCost.ToString("0.######", CultureInfo.InvariantCulture))
                .Append(",\n")
                .Append("    \"coreSize\": ")
                .Append(value.CoreSize.ToString(CultureInfo.InvariantCulture))
                .Append("\n  }")
                .Append(index == entries.Count - 1 ? '\n' : ",\n");
        }

        return text.Append("}\n").ToString();
    }

    private static (ComplexityBudget? Budget, string? Failure) Parse(
        string text,
        IReadOnlyList<string> expectedIds)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var expected = expectedIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != expected.Count
                || root.EnumerateObject().Any(property => !expected.Contains(property.Name, StringComparer.Ordinal)))
            {
                return (null, InvalidShape(expected));
            }

            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var id in expected)
            {
                if (!root.TryGetProperty(id, out var complexity)
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
                    return (null, InvalidShape(expected));
                }

                entries[id] = new Entry(propagationCost, coreSize);
            }

            return (new ComplexityBudget(entries), null);
        }
        catch (JsonException exception)
        {
            return (null, $"'{FileName}' is not readable as JSON ({exception.Message}).");
        }
    }

    private static string InvalidShape(IReadOnlyList<string> expectedIds)
        => $"'{FileName}' must contain exactly {string.Join(", ", expectedIds)} with numeric "
            + "propagationCost (0..100) and integer coreSize (>= 0).";

    internal sealed record Entry(double PropagationCost, int CoreSize)
    {
        public static Entry From(RepositoryComplexity metric)
            => new(Math.Round(metric.PropagationCostPercentage, 6, MidpointRounding.AwayFromZero), metric.CoreFiles);
    }
}
