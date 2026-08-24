namespace Harness.Structure;

/// <summary>
/// How much a dependency edge is worth as evidence: only <see cref="Proven"/> may block a run.
/// </summary>
internal enum EvidenceGrade
{
    /// <summary>A declaration position naming exactly one declaration in the repository.</summary>
    Proven,

    /// <summary>A name match the position does not pin to a single declaration.</summary>
    Inferred,
}
