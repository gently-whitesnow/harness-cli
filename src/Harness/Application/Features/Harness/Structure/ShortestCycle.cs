namespace Harness.Structure;

/// <summary>
/// The shortest cycle inside a set of nodes that all reach each other. A component of a dozen
/// modules is true and useless; the two or three modules that actually close a ring are what
/// a reader can go and break.
/// </summary>
internal static class ShortestCycle
{
    /// <summary>
    /// Nodes in cycle order, the last one closing back to the first. Both arguments must be
    /// ordered deterministically, because ties are broken by taking the first shortest found.
    /// </summary>
    public static List<int> In(IReadOnlyList<List<int>> adjacency, IReadOnlyList<int> nodes)
    {
        var members = nodes.ToHashSet();
        List<int>? shortest = null;

        foreach (var start in nodes)
        {
            var parents = Distances(adjacency, members, start);
            foreach (var node in nodes.Where(node => parents.ContainsKey(node)))
            {
                if (!adjacency[node].Contains(start))
                {
                    continue;
                }

                var candidate = PathTo(parents, node);
                if (shortest is null || candidate.Count < shortest.Count)
                {
                    shortest = candidate;
                }
            }
        }

        return shortest ?? [.. nodes];
    }

    private static Dictionary<int, int> Distances(
        IReadOnlyList<List<int>> adjacency,
        HashSet<int> members,
        int start)
    {
        var parents = new Dictionary<int, int> { [start] = start };
        var queue = new Queue<int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var next in adjacency[node].Where(members.Contains))
            {
                if (parents.TryAdd(next, node))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return parents;
    }

    private static List<int> PathTo(Dictionary<int, int> parents, int node)
    {
        var path = new List<int>();
        while (true)
        {
            path.Add(node);
            if (parents[node] == node)
            {
                path.Reverse();
                return path;
            }

            node = parents[node];
        }
    }
}
