namespace Harness.Structure;

/// <summary>Finds transitive member groups that share state.</summary>
internal sealed class MemberComponents
{
    private readonly Dictionary<string, int> indexes = new(StringComparer.Ordinal);
    private readonly int[] parents;

    private MemberComponents(IReadOnlyList<CohesionMember> members)
    {
        parents = new int[members.Count];
        for (var member = 0; member < members.Count; member++)
        {
            parents[member] = member;
            indexes[members[member].Name] = member;
        }
    }

    /// <summary>Returns mixed state/behaviour groups, largest first.</summary>
    public static List<List<string>> Of(IReadOnlyList<CohesionMember> members)
    {
        var components = new MemberComponents(members);
        for (var member = 0; member < members.Count; member++)
        {
            components.Join(member, members[member]);
        }

        return components.Groups(members);
    }

    private void Join(int member, CohesionMember declared)
    {
        foreach (var mention in declared.Mentions)
        {
            if (indexes.TryGetValue(mention, out var other) && other != member)
            {
                Union(member, other);
            }
        }
    }

    private List<List<string>> Groups(IReadOnlyList<CohesionMember> members)
    {
        var groups = new Dictionary<int, List<string>>();
        var behaviour = new HashSet<int>();
        var state = new HashSet<int>();
        for (var member = 0; member < members.Count; member++)
        {
            var root = Find(member);
            if (!groups.TryGetValue(root, out var group))
            {
                groups[root] = group = [];
            }

            group.Add(members[member].Name);
            (members[member].IsState ? state : behaviour).Add(root);
        }

        return groups
            .Where(group => behaviour.Contains(group.Key) && state.Contains(group.Key))
            .Select(group => group.Value)
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group[0], StringComparer.Ordinal)
            .ToList();
    }

    private void Union(int left, int right)
    {
        var leftRoot = Find(left);
        var rightRoot = Find(right);
        if (leftRoot != rightRoot)
        {
            parents[rightRoot] = leftRoot;
        }
    }

    private int Find(int member)
    {
        while (parents[member] != member)
        {
            parents[member] = parents[parents[member]];
            member = parents[member];
        }

        return member;
    }
}
