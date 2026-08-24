namespace Harness.Structure;

/// <summary>
/// The dependency graph between modules, collapsed from the references between the types they
/// declare. A module never depends on itself here: what a module does internally is its own
/// business, and only what crosses a boundary is a dependency.
/// </summary>
internal sealed class ModuleGraph
{
    private readonly List<string> modules = [];
    private readonly Dictionary<string, int> indexes = new(StringComparer.Ordinal);
    private readonly List<List<int>> adjacency = [];
    private readonly Dictionary<(int From, int To), ReferenceEdge> representatives = [];

    private ModuleGraph()
    {
    }

    public static List<ModuleCycle> Cycles(IEnumerable<ReferenceEdge> edges)
    {
        var graph = new ModuleGraph();
        foreach (var edge in edges)
        {
            graph.Add(edge);
        }

        return graph.Components();
    }

    private void Add(ReferenceEdge edge)
    {
        if (ModuleBoundary.Between(edge.From.Module, edge.To.Module) is not { } crossed)
        {
            return;
        }

        var from = IndexOf(crossed.From);
        var to = IndexOf(crossed.To);
        if (representatives.TryAdd((from, to), edge))
        {
            adjacency[from].Add(to);
            return;
        }

        // The earliest location keeps the reported example stable between runs.
        if (string.CompareOrdinal(edge.Location, representatives[(from, to)].Location) < 0)
        {
            representatives[(from, to)] = edge;
        }
    }

    private int IndexOf(string module)
    {
        if (indexes.TryGetValue(module, out var found))
        {
            return found;
        }

        indexes[module] = modules.Count;
        modules.Add(module);
        adjacency.Add([]);
        return modules.Count - 1;
    }

    private List<ModuleCycle> Components()
    {
        foreach (var neighbours in adjacency)
        {
            neighbours.Sort((left, right) => string.CompareOrdinal(modules[left], modules[right]));
        }

        return StronglyConnectedComponents.Of(adjacency)
            .Where(component => component.Count > 1)
            .Select(Describe)
            .OrderBy(cycle => cycle.Modules[0], StringComparer.Ordinal)
            .ToList();
    }

    private ModuleCycle Describe(List<int> component)
    {
        var nodes = component
            .OrderBy(node => modules[node], StringComparer.Ordinal)
            .ToList();
        var ring = ShortestCycle.In(adjacency, nodes);
        var path = new List<ReferenceEdge>();
        for (var step = 0; step < ring.Count; step++)
        {
            path.Add(representatives[(ring[step], ring[(step + 1) % ring.Count])]);
        }

        return new ModuleCycle(
            nodes.Select(node => modules[node]).ToList(),
            ring.Select(node => modules[node]).ToList(),
            path);
    }
}
