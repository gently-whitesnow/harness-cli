using System.Globalization;
using System.Text;
using System.Text.Json;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal sealed record ComplexityBudget(IReadOnlyDictionary<string, ComplexityBudget.Entry> Entries)
{
    public const string FileName = ".harness.budget.json";

    /// <summary>An improvement smaller than this is noise between two honest runs, not progress.</summary>
    public const double NoticeableReachDelta = 0.1;

    private const string LegacyKey = "propagationCost";

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
        string path,
        IReadOnlyList<string> expectedIds)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path), expectedIds) : (null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not read '{FileName}': {exception.Message}");
        }
    }

    /// <summary>A budget written by contract 2.5 or earlier, which recorded propagation cost.</summary>
    public static bool IsLegacy(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(entry =>
                    entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty(LegacyKey, out _));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
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
                .Append("    \"meanReach\": ")
                .Append(value.MeanReach.ToString("0.######", CultureInfo.InvariantCulture))
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
                if (!root.TryGetProperty(id, out var complexity) || complexity.ValueKind != JsonValueKind.Object)
                {
                    return (null, InvalidShape(expected));
                }

                if (complexity.TryGetProperty(LegacyKey, out _))
                {
                    return (null, $"'{FileName}' records propagationCost from contract 2.5 or earlier; "
                        + "contract 2.6 budgets mean reach — run `harness budget update`, review it, and commit the file.");
                }

                if (complexity.EnumerateObject().Count() != 2
                    || !complexity.TryGetProperty("meanReach", out var reach)
                    || !reach.TryGetDouble(out var meanReach)
                    || !double.IsFinite(meanReach)
                    || meanReach < 0
                    || !complexity.TryGetProperty("coreSize", out var core)
                    || !core.TryGetInt32(out var coreSize)
                    || coreSize < 0)
                {
                    return (null, InvalidShape(expected));
                }

                entries[id] = new Entry(meanReach, coreSize);
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
            + "meanReach (>= 0) and integer coreSize (>= 0).";

    internal sealed record Entry(double MeanReach, int CoreSize)
    {
        public static Entry From(RepositoryComplexity metric)
            => new(Math.Round(metric.MeanReach, 6, MidpointRounding.AwayFromZero), metric.CoreFiles);
    }
}
