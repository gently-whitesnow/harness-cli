namespace Harness.Structure;

/// <summary>
/// What one language contributed: the types a repository declares, the references between
/// them that could be resolved inside it, and how much of the candidate traffic that was.
/// Nothing outside the repository appears here as a node — a framework or package type
/// cannot be resolved from source, so it is counted as an import and never as an edge.
/// </summary>
internal sealed record SourceGraph(
    IReadOnlyList<TypeNode> Types,
    IReadOnlyList<ReferenceEdge> Edges,
    IReadOnlyList<ExternalImports> Imports,
    int ResolvedReferences,
    int AmbiguousReferences)
{
    public int CandidateReferences => ResolvedReferences + AmbiguousReferences;

    /// <summary>
    /// The share of name matches the reader could attribute to exactly one declaration.
    /// A graph with nothing to resolve is complete, not empty.
    /// </summary>
    public int CoveragePercentage
        => CandidateReferences == 0 ? 100 : (int)(100L * ResolvedReferences / CandidateReferences);

    public IEnumerable<ReferenceEdge> Proven
        => Edges.Where(edge => edge.Grade == EvidenceGrade.Proven);
}
