namespace Harness.Structure;

/// <summary>
/// Where sliced-dotnet/1 says the repository's own product lives. A directory that contains
/// Application/ starts a zone, and the canonical layers directly below it hold the authored
/// code; everything else — tests, tooling, samples — sits outside. The judgement is read from
/// the tree alone, so no answer in the frame can move a file across this line.
/// </summary>
internal static class ArchitectureZones
{
    public static readonly string[] Layers =
        ["Host", "Api", "Consumers", "Application", "Domain", "Infrastructure", "Shared"];

    public static List<string> Discover(IReadOnlyList<string> paths)
    {
        var candidates = paths
            .SelectMany(path => Candidates(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(zone => zone.Count(character => character == '/'))
            .ThenBy(zone => zone, StringComparer.Ordinal)
            .ToList();

        var zones = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!zones.Any(zone => IsInsideExistingLayer(candidate, zone)))
            {
                zones.Add(candidate);
            }
        }

        return zones;
    }

    /// <summary>The path relative to the zone, or a path starting with "../" when it lies outside.</summary>
    public static string Relative(string path, string zone)
    {
        if (zone.Length == 0)
        {
            return path;
        }

        var prefix = zone + "/";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : "../";
    }

    public static bool Contains(IReadOnlyList<string> zones, string path)
        => zones.Any(zone => !Relative(path, zone).StartsWith("../", StringComparison.Ordinal));

    public static string Display(string zone) => zone.Length == 0 ? "." : zone;

    private static IEnumerable<string> Candidates(string path)
    {
        var parts = path.Split('/');
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (parts[index] == "Application")
            {
                yield return string.Join('/', parts.Take(index));
            }
        }
    }

    private static bool IsInsideExistingLayer(string candidate, string zone)
    {
        var relative = Relative(candidate, zone);
        var first = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null && Layers.Contains(first, StringComparer.Ordinal);
    }
}
