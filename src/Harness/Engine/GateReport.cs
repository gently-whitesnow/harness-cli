using Harness.Checks;

namespace Harness.Engine;

internal sealed record GateReport(
    string Id,
    string Summary,
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    TimeSpan Duration,
    string? OutcomeReason,
    IReadOnlyList<SuppressedFinding> Suppressed);
