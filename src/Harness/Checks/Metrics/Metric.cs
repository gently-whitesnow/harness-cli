namespace Harness.Checks.Metrics;

/// <summary>
/// A named formula and the number a value of it is reported against. The name is the whole
/// claim: it says what was counted and nothing that does not follow from counting it. The
/// number is a comparison point, not a threshold and not a budget.
/// </summary>
internal sealed record Metric(string Name, int ComparisonPoint);
