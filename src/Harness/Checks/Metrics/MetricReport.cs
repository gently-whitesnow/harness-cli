namespace Harness.Checks.Metrics;

/// <summary>
/// Turns measurements into the report the console prints: the worst subjects of each metric
/// and a count of the rest. A repository inventory is not a finding, and a list nobody reads
/// to the end reports nothing.
/// </summary>
internal static class MetricReport
{
    public static MetricFindings Exceeding(
        IReadOnlyList<Measurement> measurements,
        IReadOnlyList<Metric> metrics,
        int shown,
        FindingSeverity severity = FindingSeverity.Advisory)
    {
        var findings = new List<Finding>();
        var detailed = new List<Finding>();
        foreach (var metric in metrics)
        {
            var all = Exceeding(measurements, metric, severity);
            detailed.AddRange(all);
            findings.AddRange(all.Take(shown));

            if (all.Count > shown)
            {
                findings.Add(new Finding(
                    severity,
                    all[shown].Location,
                    $"{metric.Name}: {all.Count} subjects exceed the configured comparison point "
                        + $"of {metric.ComparisonPoint}; the {shown} largest are listed above"));
            }
        }

        return new MetricFindings(findings, detailed);
    }

    private static List<Finding> Exceeding(
        IReadOnlyList<Measurement> measurements,
        Metric metric,
        FindingSeverity severity)
    {
        var exceeded = measurements
            .Where(measurement => ReferenceEquals(measurement.Metric, metric))
            .Where(measurement => measurement.Value > metric.ComparisonPoint)
            .OrderByDescending(measurement => measurement.Value)
            .ThenBy(measurement => measurement.Location, StringComparer.Ordinal)
            .ToList();

        return exceeded
            .Select(measurement => new Finding(
                severity,
                measurement.Location,
                $"{metric.Name} {measurement.Value} exceeds the configured comparison point "
                    + $"of {metric.ComparisonPoint} in {measurement.Subject}"))
            .ToList();
    }
}
