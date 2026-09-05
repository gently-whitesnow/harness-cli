namespace Harness.Checks;

internal interface IRepositoryCheck
{
    string Id { get; }

    string Group { get; }

    string? Applicability => null;

    /// <summary>
    /// Files this check looks up by name and reports as missing; empty when it names none.
    /// Required with no default, so ADR-0026 cannot be lost in the next check somebody writes.
    /// </summary>
    IReadOnlyList<EvidenceFile> Evidence { get; }

    string Summary { get; }

    string Explanation { get; }

    CheckEvaluation Evaluate(CheckContext context);
}
