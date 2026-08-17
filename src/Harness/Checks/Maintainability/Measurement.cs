namespace Harness.Checks.Maintainability;

internal sealed record Measurement(MaintainabilityMetric Metric, int Value, string Subject, string Location);
