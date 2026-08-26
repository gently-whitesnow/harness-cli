namespace Harness.Checks.Metrics;

internal sealed record MetricFindings(
    IReadOnlyList<Finding> Summary,
    IReadOnlyList<Finding> Detailed);
