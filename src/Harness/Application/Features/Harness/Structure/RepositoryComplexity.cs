namespace Harness.Structure;

/// <summary>DSM measurements over authored files connected by Proven dependency edges.</summary>
internal sealed record RepositoryComplexity(
    int AuthoredFiles,
    long ReachablePairs,
    int CoreFiles)
{
    public double PropagationCostPercentage
        => AuthoredFiles == 0 ? 0 : 100.0 * ReachablePairs / ((long)AuthoredFiles * AuthoredFiles);

    public double CorePercentage
        => AuthoredFiles == 0 ? 0 : 100.0 * CoreFiles / AuthoredFiles;

    public static RepositoryComplexity Measure(SourceGraph graph)
    {
        var fileGraph = Build(graph);
        var analysis = Analyze(fileGraph.Adjacency);
        return new RepositoryComplexity(fileGraph.Paths.Count, analysis.ReachablePairs, analysis.CoreFiles);
    }

    public static IReadOnlyList<string> LargestCore(SourceGraph graph)
    {
        var fileGraph = Build(graph);
        var analysis = Analyze(fileGraph.Adjacency);
        return analysis.Components
            .Where(component => component.Count > 1)
            .OrderByDescending(component => component.Count)
            .ThenBy(component => fileGraph.Paths[component.Min()], StringComparer.Ordinal)
            .FirstOrDefault()?
            .Select(index => fileGraph.Paths[index])
            .Order(StringComparer.Ordinal)
            .ToList() ?? [];
    }

    public static IReadOnlyList<PropagationEdge> HighestPropagationEdges(SourceGraph graph)
    {
        var fileGraph = Build(graph);
        var analysis = Analyze(fileGraph.Adjacency);
        return graph.Proven
            .Where(edge => edge.From.Path != edge.To.Path)
            .GroupBy(edge => (edge.From.Path, edge.To.Path))
            .Select(group => group.First())
            .Select(edge => new PropagationEdge(
                edge,
                PropagationSpan(
                    analysis,
                    analysis.ComponentOf[fileGraph.Indexes[edge.From.Path]],
                    analysis.ComponentOf[fileGraph.Indexes[edge.To.Path]])))
            .OrderByDescending(edge => edge.ReachablePairs)
            .ThenBy(edge => edge.Edge.Location, StringComparer.Ordinal)
            .ToList();
    }

    private static FileGraph Build(SourceGraph graph)
    {
        var paths = graph.SourcePaths
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var indexes = paths
            .Select((path, index) => (path, index))
            .ToDictionary(pair => pair.path, pair => pair.index, StringComparer.Ordinal);
        var adjacency = Enumerable.Range(0, paths.Count).Select(_ => new List<int>()).ToList();

        foreach (var edge in graph.Proven)
        {
            var from = indexes[edge.From.Path];
            var to = indexes[edge.To.Path];
            if (from != to && !adjacency[from].Contains(to))
            {
                adjacency[from].Add(to);
            }
        }

        return new FileGraph(paths, indexes, adjacency);
    }

    private static Analysis Analyze(List<List<int>> adjacency)
    {
        if (adjacency.Count == 0)
        {
            return new Analysis([], [], [], 0, 0);
        }

        var components = StronglyConnectedComponents.Of(adjacency);
        var componentOf = new int[adjacency.Count];
        for (var component = 0; component < components.Count; component++)
        {
            foreach (var node in components[component])
            {
                componentOf[node] = component;
            }
        }

        var condensation = Enumerable.Range(0, components.Count).Select(_ => new HashSet<int>()).ToList();
        var indegree = new int[components.Count];
        for (var from = 0; from < adjacency.Count; from++)
        {
            foreach (var to in adjacency[from])
            {
                var fromComponent = componentOf[from];
                var toComponent = componentOf[to];
                if (fromComponent != toComponent && condensation[fromComponent].Add(toComponent))
                {
                    indegree[toComponent]++;
                }
            }
        }

        var order = TopologicalOrder(condensation, indegree);
        var words = (components.Count + 63) / 64;
        var reachable = Enumerable.Range(0, components.Count).Select(_ => new ulong[words]).ToList();
        for (var cursor = order.Count - 1; cursor >= 0; cursor--)
        {
            var component = order[cursor];
            reachable[component][component / 64] |= 1UL << (component % 64);
            foreach (var target in condensation[component])
            {
                for (var word = 0; word < words; word++)
                {
                    reachable[component][word] |= reachable[target][word];
                }
            }
        }

        long pairs = 0;
        for (var from = 0; from < components.Count; from++)
        {
            var reachableFiles = 0;
            for (var to = 0; to < components.Count; to++)
            {
                if ((reachable[from][to / 64] & (1UL << (to % 64))) != 0)
                {
                    reachableFiles += components[to].Count;
                }
            }

            pairs += (long)components[from].Count * reachableFiles;
        }

        var core = components.Where(component => component.Count > 1)
            .Select(component => component.Count)
            .DefaultIfEmpty(0)
            .Max();
        return new Analysis(components, componentOf, reachable, pairs, core);
    }

    private static long PropagationSpan(Analysis analysis, int from, int to)
    {
        var ancestors = 0;
        for (var component = 0; component < analysis.Components.Count; component++)
        {
            if (Reaches(analysis, component, from))
            {
                ancestors += analysis.Components[component].Count;
            }
        }

        var descendants = 0;
        for (var component = 0; component < analysis.Components.Count; component++)
        {
            if (Reaches(analysis, to, component))
            {
                descendants += analysis.Components[component].Count;
            }
        }

        return (long)ancestors * descendants;
    }

    private static bool Reaches(Analysis analysis, int from, int to)
        => (analysis.Reachable[from][to / 64] & (1UL << (to % 64))) != 0;

    private static List<int> TopologicalOrder(
        List<HashSet<int>> adjacency,
        IReadOnlyList<int> sourceIndegree)
    {
        var indegree = sourceIndegree.ToArray();
        var ready = new Queue<int>(Enumerable.Range(0, indegree.Length).Where(node => indegree[node] == 0));
        var order = new List<int>(indegree.Length);

        while (ready.TryDequeue(out var node))
        {
            order.Add(node);
            foreach (var child in adjacency[node])
            {
                if (--indegree[child] == 0)
                {
                    ready.Enqueue(child);
                }
            }
        }

        return order;
    }

    internal sealed record PropagationEdge(ReferenceEdge Edge, long ReachablePairs);

    private sealed record FileGraph(
        IReadOnlyList<string> Paths,
        IReadOnlyDictionary<string, int> Indexes,
        List<List<int>> Adjacency);

    private sealed record Analysis(
        IReadOnlyList<List<int>> Components,
        IReadOnlyList<int> ComponentOf,
        IReadOnlyList<ulong[]> Reachable,
        long ReachablePairs,
        int CoreFiles);
}
