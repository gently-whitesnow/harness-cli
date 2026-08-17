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

internal interface IRepositoryCheck
{
    string Id { get; }

    string Group { get; }

    string? Applicability => null;

    string Summary { get; }

    string Explanation { get; }

    CheckEvaluation Evaluate(CheckContext context);
}
