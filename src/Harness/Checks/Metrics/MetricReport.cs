namespace Harness.Checks.Metrics;

/// <summary>
/// Turns measurements into the report the console prints: the worst subjects of each metric
/// and a count of the rest. A repository inventory is not a finding, and a list nobody reads
/// to the end reports nothing.
/// </summary>
internal static class MetricReport
{
    public static List<Finding> Exceeding(
        IReadOnlyList<Measurement> measurements,
        IReadOnlyList<Metric> metrics,
        int shown)
    {
        var findings = new List<Finding>();
        foreach (var metric in metrics)
        {
            findings.AddRange(Exceeding(measurements, metric, shown));
        }

        return findings;
    }

    // Measurements are matched by metric name and judged against their own comparison
    // point: a path-scoped override can hand two files of one repository different numbers
    // for the same formula.
    private static IEnumerable<Finding> Exceeding(
        IReadOnlyList<Measurement> measurements,
        Metric metric,
        int shown)
    {
        var exceeded = measurements
            .Where(measurement => string.Equals(measurement.Metric.Name, metric.Name, StringComparison.Ordinal))
            .Where(measurement => measurement.Value > measurement.Metric.ComparisonPoint)
            .OrderByDescending(measurement => measurement.Value)
            .ThenBy(measurement => measurement.Location, StringComparer.Ordinal)
            .ToList();

        foreach (var measurement in exceeded.Take(shown))
        {
            yield return new Finding(
                FindingSeverity.Advisory,
                measurement.Location,
                $"{metric.Name} {measurement.Value} exceeds the advisory comparison point "
                    + $"of {measurement.Metric.ComparisonPoint} in {measurement.Subject}");
        }

        if (exceeded.Count > shown)
        {
            var points = exceeded.Select(measurement => measurement.Metric.ComparisonPoint).Distinct().ToList();
            yield return new Finding(
                FindingSeverity.Advisory,
                exceeded[shown].Location,
                $"{metric.Name}: {exceeded.Count} subjects exceed "
                    + (points.Count == 1
                        ? $"the advisory comparison point of {points[0]}"
                        : "their advisory comparison points")
                    + $"; the {shown} largest are listed above");
        }
    }
}
