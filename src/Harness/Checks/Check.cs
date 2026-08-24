using Harness.Versioning;

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

internal interface IRepositoryCheck
{
    string Id { get; }

    string Group { get; }

    string? Applicability => null;

    /// <summary>
    /// The release that introduced this check. A repository pinned to an older release does
    /// not run it, so taking a newer harness never adds a finding the repository has not
    /// asked for; `harness upgrade` is what takes one on.
    /// </summary>
    HarnessVersion Since => HarnessVersion.Initial;

    string Summary { get; }

    string Explanation { get; }

    CheckEvaluation Evaluate(CheckContext context);
}
