namespace Harness.Structure;

/// <summary>
/// What one language contributed: the types a repository declares and the references between
/// them that resolve inside it. A framework or package type cannot be resolved from source,
/// so it is counted as an import and never appears as a node.
/// </summary>
internal sealed record SourceGraph(
    IReadOnlyList<string> SourcePaths,
    IReadOnlyList<TypeNode> Types,
    IReadOnlyList<ReferenceEdge> Edges,
    IReadOnlyList<ExternalImports> Imports,
    int ResolvedReferences,
    int AmbiguousReferences,
    IReadOnlyList<string> MarkedGenerated)
{
    public int CandidateReferences => ResolvedReferences + AmbiguousReferences;

    /// <summary>A graph with nothing to resolve is complete, not empty.</summary>
    public int CoveragePercentage
        => CandidateReferences == 0 ? 100 : (int)(100L * ResolvedReferences / CandidateReferences);

    public IEnumerable<ReferenceEdge> Proven
        => Edges.Where(edge => edge.Grade == EvidenceGrade.Proven);

    /// <summary>The graph over a subset of its files; an edge survives only with both ends inside.</summary>
    public SourceGraph Within(Func<string, bool> keep)
        => this with
        {
            SourcePaths = SourcePaths.Where(keep).ToList(),
            Types = Types.Where(type => keep(type.Path)).ToList(),
            Edges = Edges.Where(edge => keep(edge.From.Path) && keep(edge.To.Path)).ToList(),
            Imports = Imports.Where(import => keep(import.Path)).ToList(),
            MarkedGenerated = MarkedGenerated.Where(keep).ToList(),
        };
}
