using Harness.Structure;

namespace Harness.Languages.CSharp;

/// <summary>
/// The names the repository itself declares — a closed vocabulary that makes resolution
/// possible without a compiler: a name matching nothing here is an import, not a guess.
/// </summary>
internal sealed class CSharpTypeIndex
{
    private readonly Dictionary<string, List<TypeNode>> byName = new(StringComparer.Ordinal);
    private readonly HashSet<string> modules = new(StringComparer.Ordinal);
    private readonly HashSet<string> subjects = new(StringComparer.Ordinal);

    public CSharpTypeIndex(IEnumerable<TypeNode> types)
    {
        foreach (var type in types)
        {
            if (!byName.TryGetValue(type.Name, out var declared))
            {
                byName[type.Name] = declared = [];
            }

            declared.Add(type);
            subjects.Add(type.Subject);
            if (type.Module.Length > 0)
            {
                modules.Add(type.Module);
            }
        }
    }

    public bool Knows(string name) => byName.ContainsKey(name);

    public bool IsInternal(string import) => modules.Contains(import) || subjects.Contains(import);

    /// <summary>
    /// Which declaration a name refers to, following the order C# itself uses: the containing
    /// namespace, then the namespaces that enclose it, then what the file imports. A name left
    /// with more than one candidate is reported as unresolved rather than attributed to a guess.
    /// </summary>
    public (TypeNode? Target, bool Ambiguous) Resolve(
        string name,
        string module,
        IReadOnlyCollection<string> imports)
    {
        var candidates = byName[name];
        if (candidates.Count == 1)
        {
            return (candidates[0], false);
        }

        var declared = Single(candidates.Where(type => Same(type.Module, module)));
        if (declared is not null)
        {
            return (declared, false);
        }

        var enclosing = candidates
            .Where(type => module.StartsWith(type.Module + ".", StringComparison.Ordinal))
            .GroupBy(type => type.Module.Length)
            .OrderByDescending(group => group.Key)
            .FirstOrDefault();
        if (enclosing is not null && Single(enclosing) is { } nearest)
        {
            return (nearest, false);
        }

        var imported = Single(candidates.Where(type => imports.Contains(type.Module)));
        return imported is not null ? (imported, false) : (null, true);
    }

    private static TypeNode? Single(IEnumerable<TypeNode> candidates)
    {
        var matches = candidates.Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.Ordinal);
}
