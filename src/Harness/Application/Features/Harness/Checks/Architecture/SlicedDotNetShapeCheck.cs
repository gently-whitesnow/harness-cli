using Harness.Config;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Architecture;

internal sealed class SlicedDotNetShapeCheck(ILanguageAnalyzer analyzer) : IRepositoryCheck
{
    private const int ShownDependencyGroups = 5;

    private static readonly string[] Layers =
        ["Host", "Api", "Consumers", "Application", "Domain", "Infrastructure", "Shared"];

    private static readonly string[] PlaceholderFiles = [".gitkeep", ".keep", ".gitignore"];

    private static readonly Dictionary<string, HashSet<string>> AllowedDependencies =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Host"] = [.. Layers],
            ["Api"] = ["Application", "Shared"],
            ["Consumers"] = ["Application", "Shared"],
            ["Application"] = ["Domain", "Shared"],
            ["Domain"] = ["Shared"],
            ["Infrastructure"] = ["Application", "Domain", "Shared"],
            ["Shared"] = [],
        };

    public string Id => "architecture.sliced-dotnet";

    public string Group => "architecture";

    public IReadOnlyList<EvidenceFile> Evidence => [];

    public string Summary => "sliced-dotnet zones, layers and slices";

    public string Explanation =>
        """
        Rationale
          The sliced-dotnet/1 standard makes the tracked directory tree the architecture map.
          Every application uses the same layer vocabulary, so a review does not depend on a
          repository-specific declaration.

        What it reads
          Every tracked path in Git and the non-generated C# sources used by the dependency
          graph. A directory containing Application/ starts one zone. The check discovers
          canonical layer directories and Application/Features slices, including one optional
          grouping level. Dependency edges are lexical evidence: only Proven edges can fail this
          fitness function; Inferred edges are deliberately ignored.

        What it accepts
          Every zone contains Host, Application and at least one of Api or Consumers. Every
          present layer contains a tracked file other than .gitkeep, .keep or .gitignore, and
          every directory directly below a zone is a canonical layer. The architecture map is
          printed on every attempted run, including nested paths that would otherwise disappear.

          The layer DAG is an invariant, not a score:
            Host           -> every layer
            Api            -> Application, Shared
            Consumers      -> Application, Shared
            Application    -> Domain, Shared
            Domain         -> Shared
            Infrastructure -> Application/Contracts, Domain, Shared
            Shared         -> no other layer
          References inside one layer are allowed. References between architecture zones are
          never allowed. This layer-pair stage accepts Infrastructure -> Application; the public
          API invariant separately restricts its destination to Contracts/. It also accepts all
          Infrastructure -> Domain edges at this stage; the Persistence-wide exception becomes
          distinct when slice isolation is enforced.

        Remediation
          Move application files under Host, Api, Consumers, Application, Domain, Infrastructure
          or Shared. Put use-case slices in Application/Features/<Slice>, or group them one level
          deeper as Application/Features/<Group>/<Slice> without files in the group directory.
          Turn a forbidden dependency around or move the shared concept to an allowed lower layer.
          A type-like name in a member access can look connected to the lexical reader, but that
          edge is Inferred and cannot produce this finding; use the named files to inspect a
          reported Proven declaration position.
        """;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var architecture = context.Config!.Architecture;
        if (architecture is null)
        {
            return CheckEvaluation.Incomplete(context.Config.ArchitectureFailure!);
        }
        if (!architecture.IsApplicable)
        {
            var reason = architecture.NotApplicableReason!;
            return CheckEvaluation.NotApplicable(
                $"{HarnessConfig.FileName} declares architecture not applicable — \"{reason}\".",
                [$"architecture map: not applicable — {reason}"]);
        }

        var paths = context.Repository.TrackedEntries
            .Where(entry => !entry.IsSymbolicLink)
            .Select(entry => entry.Path)
            .ToList();
        var zones = DiscoverZones(paths);
        if (zones.Count == 0)
        {
            return CheckEvaluation.From(
                [Block(".", "no architecture zone found; sliced-dotnet/1 requires a directory containing Application/")],
                observations: ["architecture map: no zones"]);
        }

        var findings = new List<Finding>();
        var maps = new List<string>();
        foreach (var zone in zones)
        {
            InspectZone(zone, paths, findings, maps);
        }

        if (findings.Count > 0)
        {
            return CheckEvaluation.From(findings, observations: maps);
        }

        var (graph, failure) = analyzer.ReadGraph(context.Repository);
        if (graph is null)
        {
            return CheckEvaluation.Incomplete(failure!, maps);
        }

        var dependencies = InspectLayerDependencies(zones, graph);
        var detailed = dependencies.Select(dependency => dependency.Finding).ToList();
        var summary = Summarize(dependencies);

        return CheckEvaluation.From(summary, detailedFindings: detailed, observations: maps);
    }

    private static List<DependencyViolation> InspectLayerDependencies(
        IReadOnlyList<string> zones,
        SourceGraph graph)
    {
        var violations = new Dictionary<DependencyEvidence, DependencyViolation>();
        foreach (var path in graph.SourcePaths)
        {
            if (Address(path, zones) is { Layer: null, Zone: { } zone, RelativePath: { } relative }
                && !relative.Contains('/'))
            {
                var group = new DependencyGroup("outside-layer", zone, null, null, null);
                var finding = Block(path,
                    $"C# file '{path}' is inside architecture zone but outside every canonical layer");
                violations[new DependencyEvidence(group, path, string.Empty)] = new(group, finding);
            }
        }

        foreach (var edge in graph.Proven)
        {
            var from = Address(edge.From.Path, zones);
            var to = Address(edge.To.Path, zones);
            if (from.Layer is null || to.Layer is null)
            {
                continue;
            }

            if (!string.Equals(from.Zone, to.Zone, StringComparison.Ordinal))
            {
                var group = new DependencyGroup(
                    "cross-zone", from.Zone!, from.Layer, to.Zone!, to.Layer);
                var finding = Block(edge.Location,
                    $"cross-zone dependency is forbidden: {from.Layer} file '{edge.From.Path}' names "
                    + $"{to.Layer} file '{edge.To.Path}' in zone '{Display(to.Zone!)}'");
                violations[new DependencyEvidence(group, edge.From.Path, edge.To.Path)] = new(group, finding);
                continue;
            }

            if (from.Layer == to.Layer || AllowedDependencies[from.Layer].Contains(to.Layer))
            {
                continue;
            }

            var layerGroup = new DependencyGroup(
                "layer-pair", from.Zone!, from.Layer, to.Zone!, to.Layer);
            var layerFinding = Block(edge.Location,
                $"layer dependency {from.Layer} -> {to.Layer} is forbidden by sliced-dotnet/1: "
                + $"'{edge.From.Path}' names '{edge.To.Path}'");
            violations[new DependencyEvidence(layerGroup, edge.From.Path, edge.To.Path)] =
                new(layerGroup, layerFinding);
        }

        return violations.Values.OrderBy(violation => violation.Finding.Location, StringComparer.Ordinal).ToList();
    }

    private static List<Finding> Summarize(IReadOnlyList<DependencyViolation> violations)
    {
        var groups = violations
            .GroupBy(violation => violation.Group)
            .Select(group =>
            {
                var first = group.First().Finding;
                var remaining = group.Count() - 1;
                var subject = group.Key.Kind == "outside-layer" ? "files" : "file pairs";
                return remaining == 0
                    ? first
                    : first with { Message = $"{first.Message}; and {remaining} more {subject}" };
            })
            .OrderBy(finding => finding.Location, StringComparer.Ordinal)
            .ToList();
        var summary = groups.Take(ShownDependencyGroups).ToList();
        if (groups.Count > ShownDependencyGroups)
        {
            summary.Add(Block(groups[ShownDependencyGroups].Location,
                $"{groups.Count} architecture dependency groups were proved; "
                + $"the first {ShownDependencyGroups} are listed above"));
        }

        return summary;
    }

    private static LayerAddress Address(string path, IReadOnlyList<string> zones)
    {
        foreach (var zone in zones.OrderByDescending(candidate => candidate.Length))
        {
            var relative = Relative(path, zone);
            if (relative.StartsWith("../", StringComparison.Ordinal))
            {
                continue;
            }

            var layer = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return new LayerAddress(
                zone,
                Layers.Contains(layer, StringComparer.Ordinal) ? layer : null,
                relative);
        }

        return new LayerAddress(null, null, null);
    }

    private static List<string> DiscoverZones(IReadOnlyList<string> paths)
    {
        var candidates = paths
            .SelectMany(path => CandidateZones(path))
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

    private static IEnumerable<string> CandidateZones(string path)
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

    private static void InspectZone(
        string zone,
        IReadOnlyList<string> paths,
        List<Finding> findings,
        List<string> maps)
    {
        var entries = paths
            .Select(path => Relative(path, zone))
            .Where(path => path.Length > 0 && !path.StartsWith("../", StringComparison.Ordinal))
            .ToList();
        var rootDirectories = entries
            .Where(path => path.Contains('/'))
            .Select(path => path.Split('/')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var presentLayers = Layers.Where(layer => rootDirectories.Contains(layer, StringComparer.Ordinal)).ToList();

        RequireLayer(zone, "Host", presentLayers, findings);
        RequireLayer(zone, "Application", presentLayers, findings);
        if (!presentLayers.Contains("Api", StringComparer.Ordinal)
            && !presentLayers.Contains("Consumers", StringComparer.Ordinal))
        {
            findings.Add(Block(Display(zone), "missing input layer: add Api/ or Consumers/"));
        }

        foreach (var layer in presentLayers)
        {
            var prefix = layer + "/";
            var nonPlaceholderFiles = entries.Any(path => path.StartsWith(prefix, StringComparison.Ordinal)
                && !PlaceholderFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal));
            if (!nonPlaceholderFiles)
            {
                findings.Add(Block(At(zone, layer), $"layer '{layer}' is empty"));
            }
        }

        foreach (var directory in rootDirectories.Except(Layers, StringComparer.Ordinal))
        {
            var closest = Layers
                .Select(layer => (Layer: layer, Distance: EditDistance(directory, layer)))
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Layer, StringComparer.Ordinal)
                .First();
            findings.Add(closest.Distance <= 2
                ? Block(At(zone, directory),
                    $"typo-in-layer-name: '{directory}' is close to canonical layer '{closest.Layer}'")
                : Block(At(zone, directory),
                    $"noncanonical-layer-directory: '{directory}' is not a sliced-dotnet/1 layer"));
        }

        var sliceMap = DiscoverSlices(entries);
        maps.Add($"architecture map: zone {Display(zone)} · layers [{string.Join(", ", presentLayers)}] "
            + $"· slices [{string.Join(", ", sliceMap.Slices)}]"
            + (sliceMap.Nested.Count == 0
                ? string.Empty
                : $" · nested [{string.Join(", ", sliceMap.Nested)}]"));
    }

    private static SliceMap DiscoverSlices(IReadOnlyList<string> entries)
    {
        const string prefix = "Application/Features/";
        var featurePaths = entries
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..].Split('/'))
            .Where(parts => parts.Length >= 2)
            .ToList();
        var slices = new HashSet<string>(StringComparer.Ordinal);
        var nestedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var feature in featurePaths.GroupBy(parts => parts[0], StringComparer.Ordinal))
        {
            if (feature.Any(parts => parts.Length == 2))
            {
                slices.Add(feature.Key);
                foreach (var child in feature.Where(parts => parts.Length >= 3).Select(parts => parts[1]))
                {
                    nestedPaths.Add($"{feature.Key}/{child}");
                }

                continue;
            }

            foreach (var child in feature.Where(parts => parts.Length >= 3).GroupBy(parts => parts[1], StringComparer.Ordinal))
            {
                slices.Add($"{feature.Key}/{child.Key}");
                foreach (var nested in child.Where(parts => parts.Length >= 4).Select(parts => parts[2]))
                {
                    nestedPaths.Add($"{feature.Key}/{child.Key}/{nested}");
                }
            }
        }

        return new SliceMap(
            slices.Order(StringComparer.Ordinal).ToList(),
            nestedPaths.Order(StringComparer.Ordinal).ToList());
    }

    private static void RequireLayer(
        string zone,
        string layer,
        IReadOnlyList<string> present,
        List<Finding> findings)
    {
        if (!present.Contains(layer, StringComparer.Ordinal))
        {
            findings.Add(Block(Display(zone), $"missing required layer '{layer}'"));
        }
    }

    private static string Relative(string path, string zone)
    {
        if (zone.Length == 0)
        {
            return path;
        }

        var prefix = zone + "/";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : "../";
    }

    private static string Display(string zone) => zone.Length == 0 ? "." : zone;

    private static string At(string zone, string name) => zone.Length == 0 ? name : $"{zone}/{name}";

    private static Finding Block(string location, string message)
        => new(FindingSeverity.Blocking, location, message);

    private sealed record SliceMap(List<string> Slices, List<string> Nested);

    private sealed record LayerAddress(string? Zone, string? Layer, string? RelativePath);

    private sealed record DependencyGroup(
        string Kind,
        string FromZone,
        string? FromLayer,
        string? ToZone,
        string? ToLayer);

    private sealed record DependencyEvidence(DependencyGroup Group, string FromPath, string ToPath);

    private sealed record DependencyViolation(DependencyGroup Group, Finding Finding);

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            var current = new int[right.Length + 1];
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var cost = char.ToUpperInvariant(left[leftIndex - 1]) == char.ToUpperInvariant(right[rightIndex - 1])
                    ? 0
                    : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + cost);
            }

            previous = current;
        }

        return previous[right.Length];
    }
}
