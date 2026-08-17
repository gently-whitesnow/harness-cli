namespace Harness.Structure;

/// <summary>
/// Tarjan's algorithm, iterative so that a deep graph cannot exhaust the stack. A component
/// of more than one node is a set of nodes that all reach each other: a dependency cycle.
/// </summary>
internal sealed class StronglyConnectedComponents
{
    private readonly IReadOnlyList<List<int>> adjacency;
    private readonly int[] index;
    private readonly int[] low;
    private readonly bool[] onStack;
    private readonly Stack<int> path = new();
    private readonly List<List<int>> components = [];
    private int next = 1;

    private StronglyConnectedComponents(IReadOnlyList<List<int>> adjacency)
    {
        this.adjacency = adjacency;
        index = new int[adjacency.Count];
        low = new int[adjacency.Count];
        onStack = new bool[adjacency.Count];
    }

    public static List<List<int>> Of(IReadOnlyList<List<int>> adjacency)
    {
        var search = new StronglyConnectedComponents(adjacency);
        for (var node = 0; node < adjacency.Count; node++)
        {
            if (search.index[node] == 0)
            {
                search.Explore(node);
            }
        }

        return search.components;
    }

    private void Explore(int root)
    {
        var work = new Stack<Frame>();
        Enter(root);
        work.Push(new Frame(root, 0));

        while (work.Count > 0)
        {
            var frame = work.Pop();
            if (frame.Child < adjacency[frame.Node].Count)
            {
                work.Push(frame with { Child = frame.Child + 1 });
                Descend(work, frame.Node, adjacency[frame.Node][frame.Child]);
                continue;
            }

            if (work.Count > 0)
            {
                var parent = work.Peek().Node;
                low[parent] = Math.Min(low[parent], low[frame.Node]);
            }

            if (low[frame.Node] == index[frame.Node])
            {
                Close(frame.Node);
            }
        }
    }

    private void Descend(Stack<Frame> work, int node, int child)
    {
        if (index[child] == 0)
        {
            Enter(child);
            work.Push(new Frame(child, 0));
            return;
        }

        if (onStack[child])
        {
            low[node] = Math.Min(low[node], index[child]);
        }
    }

    private void Enter(int node)
    {
        index[node] = low[node] = next++;
        path.Push(node);
        onStack[node] = true;
    }

    private void Close(int root)
    {
        var component = new List<int>();
        int node;
        do
        {
            node = path.Pop();
            onStack[node] = false;
            component.Add(node);
        }
        while (node != root);

        components.Add(component);
    }

    private readonly record struct Frame(int Node, int Child);
}
