namespace Harness.Structure;

/// <summary>
/// A set of modules that all reach each other, and the shortest ring of references inside it.
/// The evidence is the point: a cycle is only worth reporting if the reader is told which
/// lines to open to break it.
/// </summary>
internal sealed record ModuleCycle(
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Ring,
    IReadOnlyList<ReferenceEdge> Path)
{
    public string Location => Path[0].Location;

    /// <summary>The modules of the ring, closing back on the one it started from.</summary>
    public IEnumerable<string> Closed => Ring.Append(Ring[0]);
}
