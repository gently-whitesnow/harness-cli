using Harness.Git;

namespace Harness.Checks;

internal enum CheckOutcome
{
    /// <summary>The check ran and proved the repository satisfies it.</summary>
    Passed,

    /// <summary>The check ran and proved a violation.</summary>
    Failed,

    /// <summary>The check was excluded by selection and reports no evidence.</summary>
    Skipped,

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

/// <summary>What a check concluded, before the engine adds timing and identity.</summary>
internal sealed record CheckEvaluation(
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    string? IncompleteReason)
{
    public static CheckEvaluation From(IReadOnlyList<Finding> findings)
        => new(
            findings.Any(finding => finding.Severity == FindingSeverity.Blocking)
                ? CheckOutcome.Failed
                : CheckOutcome.Passed,
            findings,
            IncompleteReason: null);

    public static CheckEvaluation Incomplete(string reason)
        => new(CheckOutcome.Incomplete, [], reason);
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
