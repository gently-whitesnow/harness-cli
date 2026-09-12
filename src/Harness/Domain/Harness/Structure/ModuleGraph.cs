namespace Harness.Structure;

/// <summary>
/// The dependency graph between modules, collapsed from the references between the types they
/// declare. Namespace boundaries collapse nested modules; a flat graph keeps exact module
/// identities and self references, as required for role dependencies.
/// </summary>
internal sealed class ModuleGraph
{
    private readonly List<string> modules = [];
    private readonly Dictionary<string, int> indexes = new(StringComparer.Ordinal);
    private readonly List<List<int>> adjacency = [];
    private readonly Dictionary<(int From, int To), ReferenceEdge> representatives = [];

    private readonly bool collapseNestedModules;

    private ModuleGraph(bool collapseNestedModules)
    {
        this.collapseNestedModules = collapseNestedModules;
    }

    public static List<ModuleCycle> Cycles(IEnumerable<ReferenceEdge> edges, bool collapseNestedModules = true)
    {
        var graph = new ModuleGraph(collapseNestedModules);
        foreach (var edge in edges)
        {
            graph.Add(edge);
        }

        return graph.Components();
    }

    private void Add(ReferenceEdge edge)
    {
        var boundary = collapseNestedModules
            ? ModuleBoundary.Between(edge.From.Module, edge.To.Module)
            : (edge.From.Module, edge.To.Module);
        if (boundary is not { } crossed)
        {
            return;
        }

        var from = IndexOf(crossed.Item1);
        var to = IndexOf(crossed.Item2);
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
            .Where(component => component.Count > 1 || adjacency[component[0]].Contains(component[0]))
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
