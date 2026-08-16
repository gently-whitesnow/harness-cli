using Harness.Git;
using Harness.Processes;

namespace Harness.Checks;

internal enum CheckOutcome
{
    /// <summary>The check ran and proved the repository satisfies it.</summary>
    Passed,

    /// <summary>The check ran and proved a violation.</summary>
    Failed,

    /// <summary>The check was excluded by selection and reports no evidence.</summary>
    Skipped,

    /// <summary>
    /// The repository does not have the stack this check is about. Distinct from a failure
    /// to execute, so a heterogeneous repository stays understandable.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// The repository has the stack this check is about but not the quality command the
    /// check would run. Missing infrastructure is neither a code violation nor permission
    /// for the harness to invent a repository-specific command, so it is reported as its
    /// own visible state that never reads as a pass.
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

/// <summary>One external command a check ran, as a reader can reproduce and cost it.</summary>
internal sealed record ExecutedCommand(string DisplayCommand, int ExitCode, TimeSpan Duration)
{
    public static ExecutedCommand From(ProcessResult result)
        => new(result.DisplayCommand, result.ExitCode, result.Duration);
}

/// <summary>What a check concluded, before the engine adds timing and identity.</summary>
/// <param name="OutcomeReason">Why the check is incomplete or not applicable; absent otherwise.</param>
/// <param name="Commands">External commands the check ran, in the order it ran them.</param>
internal sealed record CheckEvaluation(
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    string? OutcomeReason,
    IReadOnlyList<ExecutedCommand> Commands)
{
    public static CheckEvaluation From(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<ExecutedCommand>? commands = null)
        => new(
            findings.Any(finding => finding.Severity == FindingSeverity.Blocking)
                ? CheckOutcome.Failed
                : CheckOutcome.Passed,
            findings,
            OutcomeReason: null,
            commands ?? []);

    public static CheckEvaluation Incomplete(string reason, IReadOnlyList<ExecutedCommand>? commands = null)
        => new(CheckOutcome.Incomplete, [], reason, commands ?? []);

    public static CheckEvaluation NotApplicable(string reason)
        => new(CheckOutcome.NotApplicable, [], reason, []);

    public static CheckEvaluation ReadinessGap(string reason)
        => new(CheckOutcome.ReadinessGap, [], reason, []);
}

/// <summary>A check as the engine sees it: identity, applicability and evaluation.</summary>
internal interface IRepositoryCheck
{
    /// <summary>Stable identifier; the contract for selection, suppression and review.</summary>
    string Id { get; }

    /// <summary>Group identifier accepted by --only and --skip alongside the check identifier.</summary>
    string Group { get; }

    /// <summary>One-line description shown in normal output.</summary>
    string Summary { get; }

    /// <summary>Rationale, evidence interpretation and remediation shown by `explain`.</summary>
    string Explanation { get; }

    CheckEvaluation Evaluate(GitRepository repository);
}
