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

    private static IEnumerable<Finding> Exceeding(
        IReadOnlyList<Measurement> measurements,
        Metric metric,
        int shown)
    {
        var exceeded = measurements
            .Where(measurement => ReferenceEquals(measurement.Metric, metric))
            .Where(measurement => measurement.Value > metric.ComparisonPoint)
            .OrderByDescending(measurement => measurement.Value)
            .ThenBy(measurement => measurement.Location, StringComparer.Ordinal)
            .ToList();

        foreach (var measurement in exceeded.Take(shown))
        {
            yield return new Finding(
                FindingSeverity.Advisory,
                measurement.Location,
                $"{metric.Name} {measurement.Value} exceeds the configured comparison point "
                    + $"of {metric.ComparisonPoint} in {measurement.Subject}");
        }

        if (exceeded.Count > shown)
        {
            yield return new Finding(
                FindingSeverity.Advisory,
                exceeded[shown].Location,
                $"{metric.Name}: {exceeded.Count} subjects exceed the configured comparison point "
                    + $"of {metric.ComparisonPoint}; the {shown} largest are listed above");
        }
    }
}
