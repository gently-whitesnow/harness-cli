namespace Harness.Checks.Metrics;

internal sealed record Measurement(Metric Metric, int Value, string Subject, string Location);
