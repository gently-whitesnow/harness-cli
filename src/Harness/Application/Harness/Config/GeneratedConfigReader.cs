using System.Text.Json;

namespace Harness.Config;

internal static class GeneratedConfigReader
{
    public static (IReadOnlyList<GeneratedDeclaration>? Generated, string? Failure) Read(JsonElement root)
    {
        if (!root.TryGetProperty("generated", out var entries))
        {
            return ([], null);
        }

        if (entries.ValueKind != JsonValueKind.Array)
        {
            return (null, "'generated' must be an array of { paths, reason } entries");
        }

        var result = new List<GeneratedDeclaration>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || entry.EnumerateObject().Any(property => property.Name is not ("paths" or "reason"))
                || !entry.TryGetProperty("paths", out var paths)
                || paths.ValueKind != JsonValueKind.Array
                || !entry.TryGetProperty("reason", out var reason)
                || reason.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(reason.GetString()))
            {
                return (null, "each 'generated' entry requires only nonempty 'paths' and 'reason'");
            }

            var names = new List<string>();
            foreach (var path in paths.EnumerateArray())
            {
                if (path.ValueKind != JsonValueKind.String || path.GetString() is not { } name
                    || name.Length == 0 || name.StartsWith('/') || name.EndsWith('/') || name.Contains('\\')
                    || name.Split('/').Any(segment => segment is "" or "." or ".." || segment.Contains('*')))
                {
                    return (null, "'generated.paths' must contain normalized, nonempty paths without globs");
                }

                names.Add(name);
            }

            if (names.Count == 0 || names.Count != names.Distinct(StringComparer.Ordinal).Count())
            {
                return (null, "'generated.paths' must contain unique paths");
            }

            result.Add(new GeneratedDeclaration(names, reason.GetString()!.Trim()));
        }

        var allPaths = result.SelectMany(entry => entry.Paths).ToList();
        if (allPaths.Any(path => allPaths.Any(other => other != path
            && path.StartsWith(other + "/", StringComparison.Ordinal)))
            || allPaths.Distinct(StringComparer.Ordinal).Count() != allPaths.Count)
        {
            return (null, "'generated.paths' must not overlap or repeat across entries");
        }

        return (result, null);
    }
}
