namespace Harness.Contracts.Engine;

internal sealed record GateReport(
    string Id,
    string Summary,
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<Finding> DetailedFindings,
    TimeSpan Duration,
    string? OutcomeReason,
    IReadOnlyList<string> Observations);
