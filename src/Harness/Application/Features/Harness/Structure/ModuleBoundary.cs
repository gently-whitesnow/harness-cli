namespace Harness.Structure;

/// <summary>
/// Which boundary a reference actually crosses. Module names nest, and a name that contains
/// another names a place inside it: a registry that composes what its own subtree declares,
/// and a subtree that uses the contracts of the place it lives in, are one unit and not two
/// that depend on each other. What crosses a boundary is what separates at the first segment
/// the two names do not share, and that pair is what the graph records.
/// </summary>
internal static class ModuleBoundary
{
    public static (string From, string To)? Between(string from, string to)
    {
        if (from.Length == 0 || to.Length == 0)
        {
            return null;
        }

        var source = from.Split('.');
        var target = to.Split('.');

        var shared = 0;
        while (shared < source.Length
            && shared < target.Length
            && string.Equals(source[shared], target[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        // One name contains the other, so the reference never left the containing module.
        return shared == source.Length || shared == target.Length
            ? null
            : (Prefix(source, shared + 1), Prefix(target, shared + 1));
    }

    private static string Prefix(string[] segments, int length) => string.Join('.', segments.Take(length));
}
