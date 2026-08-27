namespace Harness.Structure;

/// <summary>
/// One type naming another, and the line that names it. The line is what a reader opens, so
/// it is the reference site and not the declaration of either end.
/// </summary>
internal sealed record ReferenceEdge(TypeNode From, TypeNode To, EvidenceGrade Grade, int Line)
{
    public string Location => $"{From.Path}:{Line}";
}
