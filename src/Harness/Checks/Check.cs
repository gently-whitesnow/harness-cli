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
    Observation,
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

    /// <summary>
    /// Files this check looks up by name and reports as missing; empty when it names none.
    /// Required with no default, so ADR-0026 cannot be lost in the next check somebody writes.
    /// </summary>
    IReadOnlyList<EvidenceFile> Evidence { get; }

    string Summary { get; }

    string Explanation { get; }

    CheckEvaluation Evaluate(CheckContext context);
}
