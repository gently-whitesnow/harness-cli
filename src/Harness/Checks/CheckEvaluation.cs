namespace Harness.Checks;

/// <param name="OutcomeReason">
/// What the outcome means for this repository, in one line: why the check is incomplete,
/// not applicable or a readiness gap, or what a check that passed established and did not.
/// Absent when the outcome speaks for itself.
/// </param>
internal sealed record CheckEvaluation(
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    string? OutcomeReason,
    IReadOnlyList<Finding> DetailedFindings)
{
    public static CheckEvaluation From(
        IReadOnlyList<Finding> findings,
        string? reason = null,
        IReadOnlyList<Finding>? detailedFindings = null)
        => new(
            findings.Any(finding => finding.Severity == FindingSeverity.Blocking)
                ? CheckOutcome.Failed
                : CheckOutcome.Passed,
            findings,
            reason,
            detailedFindings ?? findings);

    public static CheckEvaluation Passed(string reason) => new(CheckOutcome.Passed, [], reason, []);

    public static CheckEvaluation Incomplete(string reason) => new(CheckOutcome.Incomplete, [], reason, []);

    public static CheckEvaluation Skipped(string reason) => new(CheckOutcome.Skipped, [], reason, []);

    public static CheckEvaluation NotApplicable(string reason) => new(CheckOutcome.NotApplicable, [], reason, []);

    public static CheckEvaluation ReadinessGap(string reason) => new(CheckOutcome.ReadinessGap, [], reason, []);
}
