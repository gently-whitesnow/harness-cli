namespace Harness.Contracts.Checks;

internal enum CheckOutcome
{
    Passed,
    Failed,
    Skipped,
    NotApplicable,
    ReadinessGap,
    Incomplete,
}

internal enum FindingSeverity
{
    Blocking,
    Advisory,
}

internal sealed record CheckSummary(string Id, string Group, string Summary);
