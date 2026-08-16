using Harness.Config;
using Harness.Git;

namespace Harness.Checks;

internal enum CheckOutcome
{
    /// <summary>The check ran and proved the repository satisfies it.</summary>
    Passed,

    /// <summary>The check ran and proved a violation.</summary>
    Failed,

    /// <summary>The check was excluded by selection or by policy and reports no evidence.</summary>
    Skipped,

    /// <summary>
    /// The repository does not have the stack this check is about, or has answered the
    /// question inapplicable. Distinct from a failure to execute, so a heterogeneous
    /// repository stays understandable.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// The repository reports that expected machinery is absent. Absence is neither a code
    /// violation nor a pass, so it remains visible without changing the exit code unless
    /// repository policy makes the check required.
    /// </summary>
    ReadinessGap,

    /// <summary>The check could not be completed reliably.</summary>
    Incomplete,
}

internal enum FindingSeverity
{
    /// <summary>Proven violation; makes the run fail.</summary>
    Blocking,

    /// <summary>Needs human judgement; never fails the run on its own.</summary>
    Advisory,
}

/// <param name="Location">Repository-relative path the finding is about.</param>
internal sealed record Finding(FindingSeverity Severity, string Location, string Message);

/// <summary>A finding the repository accepted in writing, and the sentence that accepted it.</summary>
internal sealed record SuppressedFinding(Finding Finding, Suppression Suppression);

/// <summary>What a check concluded, before the engine adds timing, policy and identity.</summary>
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

    /// <summary>
    /// The check ran, found nothing wrong, and has something the reader still needs to know
    /// about what it did and did not establish.
    /// </summary>
    public static CheckEvaluation Passed(string reason)
        => new(CheckOutcome.Passed, [], reason);

    public static CheckEvaluation Incomplete(string reason)
        => new(CheckOutcome.Incomplete, [], reason);

    public static CheckEvaluation NotApplicable(string reason)
        => new(CheckOutcome.NotApplicable, [], reason);

    public static CheckEvaluation ReadinessGap(string reason)
        => new(CheckOutcome.ReadinessGap, [], reason);
}

/// <summary>
/// What one check may read while it runs: the shared Git inventory and the repository's
/// self-reported harness frame. Individual checks decide which source is in their scope.
/// </summary>
internal sealed class CheckContext(GitRepository repository, HarnessConfig? config, string? configFailure)
{
    public GitRepository Repository { get; } = repository;

    /// <summary>The repository's frame, or null when it has none the harness could read.</summary>
    public HarnessConfig? Config { get; } = config;

    /// <summary>Why the frame could not be read; null exactly when <see cref="Config"/> is not.</summary>
    public string? ConfigFailure { get; } = configFailure;
}

/// <summary>A check as the engine sees it: identity, applicability and evaluation.</summary>
internal interface IRepositoryCheck
{
    /// <summary>Stable identifier; the contract for selection, policy, suppression and review.</summary>
    string Id { get; }

    /// <summary>Group identifier accepted by --only, --skip, policy and suppression.</summary>
    string Group { get; }

    /// <summary>One-line description shown in normal output.</summary>
    string Summary { get; }

    /// <summary>Rationale, evidence interpretation and remediation shown by `explain`.</summary>
    string Explanation { get; }

    CheckEvaluation Evaluate(CheckContext context);
}
