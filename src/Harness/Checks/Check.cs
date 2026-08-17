using Harness.Config;
using Harness.Git;

namespace Harness.Checks;

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

internal sealed record Finding(FindingSeverity Severity, string Location, string Message);

internal sealed record SuppressedFinding(Finding Finding, Suppression Suppression);

/// <param name="OutcomeReason">
/// What the outcome means for this repository, in one line: why the check is incomplete,
/// not applicable or a readiness gap, or what a check that passed established and did not.
/// Absent when the outcome speaks for itself.
/// </param>
internal sealed record CheckEvaluation(
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    string? OutcomeReason)
{
    public static CheckEvaluation From(IReadOnlyList<Finding> findings, string? reason = null)
        => new(
            findings.Any(finding => finding.Severity == FindingSeverity.Blocking)
                ? CheckOutcome.Failed
                : CheckOutcome.Passed,
            findings,
            reason);

    public static CheckEvaluation Passed(string reason)
        => new(CheckOutcome.Passed, [], reason);

    public static CheckEvaluation Incomplete(string reason)
        => new(CheckOutcome.Incomplete, [], reason);

    public static CheckEvaluation Skipped(string reason)
        => new(CheckOutcome.Skipped, [], reason);

    public static CheckEvaluation NotApplicable(string reason)
        => new(CheckOutcome.NotApplicable, [], reason);

    public static CheckEvaluation ReadinessGap(string reason)
        => new(CheckOutcome.ReadinessGap, [], reason);
}

internal sealed class CheckContext(GitRepository repository, HarnessConfig? config, string? configFailure)
{
    public GitRepository Repository { get; } = repository;

    public HarnessConfig? Config { get; } = config;

    public string? ConfigFailure { get; } = configFailure;
}

internal interface IRepositoryCheck
{
    string Id { get; }

    string Group { get; }

    string Summary { get; }

    string Explanation { get; }

    CheckEvaluation Evaluate(CheckContext context);
}
