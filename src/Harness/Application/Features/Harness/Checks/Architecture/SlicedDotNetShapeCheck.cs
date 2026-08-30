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

    private static readonly string[] MirrorLayers = ["Api", "Consumers", "Infrastructure", "Domain"];

    private static readonly string[] SliceDimensions = ["Application", .. MirrorLayers];

    private static readonly string[] SlicelessSegmentLayers = ["Host", "Shared"];

    // Literal port of Steiger's BAD_NAMES_GENERIC pinned by ADR-0037. Keep local policy out of this set.
    private static readonly HashSet<string> SteigerBadNamesGeneric =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Component", "Components",
            "Helper", "Helpers",
            "Util", "Utils",
            "Constant", "Constants", "Const", "Consts",
            "Type", "Types",
            "Store", "Stores",
            "Modal", "Modals",
            "Service", "Services",
            "Function", "Functions",
            "Class", "Classes",
            "Enum", "Enums",
            "Interface", "Interfaces",
            "Decorator", "Decorators",
            "Schema", "Schemas",
            "Handler", "Handlers",
            "Fixture", "Fixtures",
            "Middleware", "Middlewares",
            "Validator", "Validators", "Validation", "Validations",
            "Resolver", "Resolvers",
            "Mutation", "Mutations",
            "Asset", "Assets",
        };

    // Backend vocabulary owned by sliced-dotnet/1, not by Feature-Sliced Design or Steiger.
    private static readonly HashSet<string> BackendEssenceBasedSegmentNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Common",
            "Manager", "Managers",
            "Repository", "Repositories",
        };

    private static readonly Dictionary<string, HashSet<string>> AllowedDependencies =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Host"] = [.. Layers],
            ["Api"] = ["Application", "Domain", "Shared"],
            ["Consumers"] = ["Application", "Domain", "Shared"],
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
          repository-specific declaration. This is tier 1 of the contract 2.0 model: immutable
          topology invariants. Tier 2 is the monotonic DSM budget; tier 3 is explicit repository
          policy. ADR-0032 defines the tiers and ADR-0033 defines sliced-dotnet/1.

        What it reads
          Every tracked path in Git and the non-generated C# sources used by the dependency
          graph. A directory containing Application/ starts one zone. The check discovers
          canonical layer directories and Application/Features slices, including one optional
          grouping level. Dependency edges are lexical evidence: only Proven edges can fail this
          fitness function; Inferred edges are ignored by blocking invariants. The advisory
          insignificant-slice convention accepts both Proven and Inferred resolved edges from the
          slice's own input mirrors to avoid claiming that a referenced slice is unused.

        What it accepts
          Every zone contains Host, Application and at least one of Api or Consumers. Every
          present layer contains a tracked file other than .gitkeep, .keep or .gitignore, and
          every directory directly below a zone is a canonical layer. The architecture map is
          printed on every attempted run, including nested paths that would otherwise disappear.

          The layer DAG is an invariant, not a score:
            Host           -> every layer
            Api            -> Application, Domain, Shared
            Consumers      -> Application, Domain, Shared
            Application    -> Domain, Shared
            Domain         -> Shared
            Infrastructure -> Application/Contracts, Domain, Shared
            Shared         -> no other layer
          References inside one layer are allowed. References between architecture zones are
          never allowed. This layer-pair stage accepts Infrastructure -> Application; the public
          API invariant separately restricts its destination to Contracts/. It also accepts all
          Infrastructure -> Domain edges because Domain is the common vocabulary available to
          every upper layer.

          Slices inside one layer do not reference each other directly, including through an
          ordinary Contracts/ directory. A producer can expose a consumer-specific cross-API at
          Contracts/X/<Consumer>/ (or Domain/<Producer>/X/<Consumer>/); only that named consumer
          and Host composition may import it. Every reference into an Application slice from
          outside that slice goes through Contracts/, except Host composition. Upper layers may
          freely compose different Domain slices. Slice isolation is evaluated within one layer:
          a mirror may consume the ordinary public Contracts/ of a differently named Application
          slice.

          A file directly inside Features/<Name>/ makes <Name> a slice. Without such a file, that
          directory is a group and its child directories are the slices.

          Application/Features is the source of slice names. Every slice has a non-empty mirror in
          Api/Features or Consumers/Features. Api, Consumers, Infrastructure/Features and Domain
          mirrors cannot introduce a slice that Application does not contain. Domain/Shared and
          Infrastructure/Persistence are reserved non-slice directories. Placeholder-only slices,
          groups and mirrors are empty architecture forms and fail the check.

          Direct segments in slices and in the sliceless Host and Shared layers are named by purpose
          rather than by the kind of code they contain. The segments-by-purpose finding literally
          ports BAD_NAMES_GENERIC from the Steiger commit pinned by ADR-0037. The five backend
          additions — Common, Manager, Managers, Repository and Repositories — belong to the
          sliced-dotnet/1 policy. Steiger's frontend framework vocabularies are intentionally not
          part of this .NET standard.
          An essence-based leaf below a slice group is rejected as an ambiguous segment on a sliced
          dimension, following Steiger's no-segments-on-sliced-layers rule. The check still advises
          on slices without a resolved incoming reference from their own input mirror, mixed
          singular/plural names and more than 20 ungrouped slices.

        Policy
          A violation is blocking when the tracked policy for this check is required, and no
          path, file or finding is exempt from it. A repository that has not moved to the
          standard yet may run the whole check `advisory` or `off`; that broader decision is one
          reviewable line in the tracked frame and the report states it on every run.

        Remediation
          Move application files under Host, Api, Consumers, Application, Domain, Infrastructure
          or Shared. Put use-case slices in Application/Features/<Slice>, or group them one level
          deeper as Application/Features/<Group>/<Slice> without files in the group directory.
          Turn a forbidden dependency around or move the shared concept to an allowed lower layer.
          For a cross-slice dependency, prefer merging slices, then moving the shared concept down
          to Domain or Shared, and use an explicit X/<Consumer> cross-API only as a last resort.
          Give every Application slice a synchronous or asynchronous input mirror, remove orphaned
          mirrors, and replace placeholder-only architecture directories with working code or remove
          the dead form. Rename an essence-based segment such as Services, Validators or Repositories
          after the purpose it serves. If the name is a leaf below a slice group, either choose a
          business slice name or move the segment inside a named slice. These naming checks expose
          structural ambiguity; they do not prove semantic slice cohesion.
          A type-like name in a member access can look connected to the lexical reader. That
          Inferred edge cannot produce a blocking finding, but it can establish a reference for
          insignificant-slice; inspect the named files behind every reported Proven violation.

        Decisions
          adrs/0032-topology-over-thresholds.md
          adrs/0033-canonical-standard-over-declarations.md
          adrs/0036-input-layers-read-domain.md
          adrs/0037-segments-by-purpose.md
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

        var shapeFindings = new List<Finding>();
        var segmentFindings = new List<Finding>();
        var maps = new List<string>();
        var slicesByZone = new Dictionary<string, SliceMap>(StringComparer.Ordinal);
        foreach (var zone in zones)
        {
            var map = InspectZone(zone, paths, shapeFindings, maps);
            slicesByZone[zone] = map;
            InspectSegmentPurposes(zone, map, paths, segmentFindings);
        }

        if (shapeFindings.Count > 0)
        {
            return CheckEvaluation.From(
                [.. shapeFindings, .. segmentFindings],
                observations: maps);
        }

        var (graph, failure) = analyzer.ReadGraph(context.Repository);
        if (graph is null)
        {
            return CheckEvaluation.Incomplete(failure!, segmentFindings, maps);
        }

        var dependencies = InspectDependencies(
            zones,
            slicesByZone.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Slices),
            graph);
        maps.AddRange(InspectConventions(zones, slicesByZone, paths, graph));
        var detailed = segmentFindings
            .Concat(dependencies.Select(dependency => dependency.Finding))
            .ToList();
        var summary = segmentFindings
            .Concat(Summarize(dependencies))
            .ToList();

        return CheckEvaluation.From(summary, detailedFindings: detailed, observations: maps);
    }

    private static List<string> InspectConventions(
        IReadOnlyList<string> zones,
        IReadOnlyDictionary<string, SliceMap> slicesByZone,
        IReadOnlyList<string> paths,
        SourceGraph graph)
    {
        var observations = new List<string>();
        foreach (var zone in zones)
        {
            var map = slicesByZone[zone];
            var entries = paths
                .Select(path => Relative(path, zone))
                .Where(path => path.Length > 0 && !path.StartsWith("../", StringComparison.Ordinal))
                .ToList();

            foreach (var slice in map.Slices)
            {
                var referencedByOwnInput = graph.Edges.Any(edge =>
                {
                    var from = Address(edge.From.Path, zones);
                    var to = Address(edge.To.Path, zones);
                    return string.Equals(from.Zone, zone, StringComparison.Ordinal)
                        && from.Layer is "Api" or "Consumers"
                        && string.Equals(SliceOf(from, map.Slices)?.Name, slice, StringComparison.Ordinal)
                        && to.Layer == "Application"
                        && string.Equals(SliceOf(to, map.Slices)?.Name, slice, StringComparison.Ordinal);
                });
                if (!referencedByOwnInput)
                {
                    observations.Add(
                        $"advisory {At(zone, $"Application/Features/{slice}")}: insignificant-slice: slice '{slice}', "
                        + $"dimension 'Application', has no resolved incoming reference from its own "
                        + $"Api/Features/{slice}/ or Consumers/Features/{slice}/ mirror");
                }

            }

            AddPluralizationAdvice(zone, map, observations);
            var ungrouped = map.Slices.Where(slice => !slice.Contains('/')).ToList();
            if (ungrouped.Count > 20)
            {
                observations.Add(
                    $"advisory {At(zone, "Application/Features")}: excessive-slicing: dimension 'Application' has "
                    + $"{ungrouped.Count} ungrouped slices; group slices by business area when the flat list exceeds 20");
            }
        }

        return observations;
    }

    private static void InspectSegmentPurposes(
        string zone,
        SliceMap map,
        IReadOnlyList<string> paths,
        List<Finding> findings)
    {
        var entries = paths
            .Select(path => Relative(path, zone))
            .Where(path => path.Length > 0 && !path.StartsWith("../", StringComparison.Ordinal))
            .ToList();

        foreach (var slice in map.Slices)
        {
            var leaf = slice.Split('/')[^1];
            if (IsEssenceBasedSegmentName(leaf))
            {
                findings.Add(Advice(
                    At(zone, $"Application/Features/{slice}"),
                    $"no-segments-on-sliced-layers: application slice '{slice}' ends in essence-based "
                    + $"name '{leaf}'; choose a business slice name or move that segment inside a named slice"));
            }

            foreach (var dimension in SliceDimensions)
            {
                var slicePrefix = dimension == "Domain"
                    ? $"Domain/{slice}/"
                    : $"{dimension}/Features/{slice}/";
                foreach (var segment in entries
                    .Where(path => path.StartsWith(slicePrefix, StringComparison.Ordinal))
                    .Select(path => ImmediateDirectory(path[slicePrefix.Length..]))
                    .Where(segment => segment is not null && IsEssenceBasedSegmentName(segment))
                    .Select(segment => segment!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(Advice(
                        At(zone, $"{slicePrefix}{segment}"),
                        $"segments-by-purpose: slice '{slice}', dimension '{dimension}', segment '{segment}' "
                        + "names what its contents are; rename it after the purpose those contents serve"));
                }
            }
        }

        foreach (var layer in SlicelessSegmentLayers)
        {
            var layerPrefix = $"{layer}/";
            foreach (var segment in entries
                .Where(path => path.StartsWith(layerPrefix, StringComparison.Ordinal))
                .Select(path => ImmediateDirectory(path[layerPrefix.Length..]))
                .Where(segment => segment is not null && IsEssenceBasedSegmentName(segment))
                .Select(segment => segment!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(Advice(
                    At(zone, $"{layerPrefix}{segment}"),
                    $"segments-by-purpose: sliceless layer '{layer}', segment '{segment}' names what its "
                    + "contents are; rename it after the purpose those contents serve"));
            }
        }
    }

    private static bool IsEssenceBasedSegmentName(string name)
        => SteigerBadNamesGeneric.Contains(name)
            || BackendEssenceBasedSegmentNames.Contains(name);

    private static string? ImmediateDirectory(string relativePath)
    {
        var separator = relativePath.IndexOf('/');
        return separator > 0 ? relativePath[..separator] : null;
    }

    private static void AddPluralizationAdvice(string zone, SliceMap map, List<string> observations)
    {
        foreach (var group in map.Slices.GroupBy(
            slice => slice.Contains('/') ? slice[..slice.LastIndexOf('/')] : string.Empty,
            StringComparer.Ordinal))
        {
            var names = group.Select(slice => slice.Split('/')[^1]).ToList();
            var plural = names.Where(LooksPlural).ToList();
            var singular = names.Where(name => !LooksPlural(name)).ToList();
            if (plural.Count == 0 || singular.Count == 0)
            {
                continue;
            }

            var preference = plural.Count >= singular.Count ? "plural" : "singular";
            var location = group.Key.Length == 0
                ? At(zone, "Application/Features")
                : At(zone, $"Application/Features/{group.Key}");
            observations.Add(
                $"advisory {location}: inconsistent-slice-pluralization: dimension 'Application', slices "
                + $"[{string.Join(", ", names)}] mix singular and plural names; prefer {preference} names in this group");
        }
    }

    private static bool LooksPlural(string name)
        => name.EndsWith('s')
            && !name.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith("us", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("Status", StringComparison.OrdinalIgnoreCase);

    private static List<DependencyViolation> InspectDependencies(
        IReadOnlyList<string> zones,
        Dictionary<string, IReadOnlyList<string>> slicesByZone,
        SourceGraph graph)
    {
        var violations = new Dictionary<DependencyEvidence, DependencyViolation>();
        foreach (var path in graph.SourcePaths)
        {
            var address = Address(path, zones);
            if (address is { Layer: null, Zone: { } zone, RelativePath: { } relative }
                && !relative.Contains('/'))
            {
                var group = new DependencyGroup("outside-layer", zone, null, null, null, null, null);
                var finding = Block(path,
                    $"C# file '{path}' is inside architecture zone but outside every canonical layer");
                violations[new DependencyEvidence(group, path, string.Empty)] = new(group, finding);
            }

            if (address is { Layer: not null, Zone: { } sliceZone }
                && IsDirectlyInsideSliceRoot(address))
            {
                var group = new DependencyGroup(
                    "outside-slice", sliceZone, address.Layer, null, null, null, null);
                var finding = Block(path,
                    $"C# file '{path}' is inside slice-bearing layer '{address.Layer}' but outside every slice");
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
                    "cross-zone", from.Zone!, from.Layer, null, to.Zone!, to.Layer, null);
                var finding = Block(edge.Location,
                    $"cross-zone dependency is forbidden: {from.Layer} file '{edge.From.Path}' names "
                    + $"{to.Layer} file '{edge.To.Path}' in zone '{Display(to.Zone!)}'");
                violations[new DependencyEvidence(group, edge.From.Path, edge.To.Path)] = new(group, finding);
                continue;
            }

            if (from.Layer != to.Layer && !AllowedDependencies[from.Layer].Contains(to.Layer))
            {
                var layerGroup = new DependencyGroup(
                    "layer-pair", from.Zone!, from.Layer, null, to.Zone!, to.Layer, null);
                var layerFinding = Block(edge.Location,
                    $"layer dependency {from.Layer} -> {to.Layer} is forbidden by sliced-dotnet/1: "
                    + $"'{edge.From.Path}' names '{edge.To.Path}'");
                violations[new DependencyEvidence(layerGroup, edge.From.Path, edge.To.Path)] =
                    new(layerGroup, layerFinding);
                continue;
            }

            var slices = slicesByZone[from.Zone!];
            var fromSlice = SliceOf(from, slices);
            var toSlice = SliceOf(to, slices);
            if (SliceViolation(edge, from, fromSlice, to, toSlice, slices) is { } sliceViolation)
            {
                violations[new DependencyEvidence(
                    sliceViolation.Group, edge.From.Path, edge.To.Path)] = sliceViolation;
            }
        }

        return violations.Values.OrderBy(violation => violation.Finding.Location, StringComparer.Ordinal).ToList();
    }

    private static DependencyViolation? SliceViolation(
        ReferenceEdge edge,
        LayerAddress from,
        SliceAddress? fromSlice,
        LayerAddress to,
        SliceAddress? toSlice,
        IReadOnlyList<string> knownSlices)
    {
        if (toSlice is null)
        {
            return null;
        }

        var crossConsumer = CrossConsumer(to, toSlice, knownSlices);
        if (crossConsumer is not null
            && from.Layer != "Host"
            && !string.Equals(fromSlice?.Name, crossConsumer, StringComparison.Ordinal))
        {
            var group = new DependencyGroup(
                "cross-api-consumer", from.Zone!, from.Layer, fromSlice?.Name,
                to.Zone!, to.Layer, toSlice.Name);
            var importer = fromSlice is null ? $"{from.Layer} outside a slice" : $"slice '{fromSlice.Name}'";
            var finding = Block(edge.Location,
                $"cross-API '{toSlice.Name}/{CrossApiPath(to.Layer!, crossConsumer)}' may be imported only "
                + $"by slice '{crossConsumer}'; {importer} file '{edge.From.Path}' names '{edge.To.Path}'");
            return new DependencyViolation(group, finding);
        }

        if (from.Layer == to.Layer
            && fromSlice is not null
            && !string.Equals(fromSlice.Name, toSlice.Name, StringComparison.Ordinal)
            && crossConsumer is null)
        {
            var group = new DependencyGroup(
                "slice-pair", from.Zone!, from.Layer, fromSlice.Name,
                to.Zone!, to.Layer, toSlice.Name);
            var finding = Block(edge.Location,
                $"cross-slice dependency {from.Layer}/{fromSlice.Name} -> {to.Layer}/{toSlice.Name} is forbidden: "
                + $"'{edge.From.Path}' names '{edge.To.Path}'; merge the slices, move the shared concept down, "
                + $"or expose {toSlice.Name}/{CrossApiPath(to.Layer!, fromSlice.Name)} as a last resort");
            return new DependencyViolation(group, finding);
        }

        if (to.Layer == "Application"
            && from.Layer != "Host"
            && !(from.Layer == "Application"
                && string.Equals(fromSlice?.Name, toSlice.Name, StringComparison.Ordinal))
            && !toSlice.Path.StartsWith("Contracts/", StringComparison.Ordinal))
        {
            var group = new DependencyGroup(
                "application-public-api", from.Zone!, from.Layer, fromSlice?.Name,
                to.Zone!, to.Layer, toSlice.Name);
            var finding = Block(edge.Location,
                $"Application public API sidestep into slice '{toSlice.Name}': '{edge.From.Path}' names "
                + $"internal file '{edge.To.Path}'; cross the slice boundary through Contracts/");
            return new DependencyViolation(group, finding);
        }

        return null;
    }

    private static SliceAddress? SliceOf(LayerAddress address, IReadOnlyList<string> knownSlices)
    {
        var prefix = address.Layer switch
        {
            "Api" or "Consumers" or "Application" or "Infrastructure" => $"{address.Layer}/Features/",
            "Domain" => "Domain/",
            _ => null,
        };
        if (prefix is null || !address.RelativePath!.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var inside = address.RelativePath[prefix.Length..];
        if (!inside.Contains('/'))
        {
            return null;
        }

        var slice = knownSlices
            .OrderByDescending(candidate => candidate.Length)
            .FirstOrDefault(candidate => inside.StartsWith(candidate + "/", StringComparison.Ordinal));
        slice ??= inside.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (slice is null || (address.Layer == "Domain" && slice == "Shared"))
        {
            return null;
        }

        return new SliceAddress(slice, inside[(slice.Length + 1)..]);
    }

    private static bool IsDirectlyInsideSliceRoot(LayerAddress address)
    {
        var prefix = address.Layer switch
        {
            "Api" or "Consumers" or "Application" or "Infrastructure" => $"{address.Layer}/Features/",
            "Domain" => "Domain/",
            _ => null,
        };
        return prefix is not null
            && address.RelativePath!.StartsWith(prefix, StringComparison.Ordinal)
            && !address.RelativePath[prefix.Length..].Contains('/');
    }

    private static string? CrossConsumer(
        LayerAddress target,
        SliceAddress slice,
        IReadOnlyList<string> knownSlices)
    {
        var prefix = target.Layer == "Domain" ? "X/" : "Contracts/X/";
        if (!slice.Path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var consumerPath = slice.Path[prefix.Length..];
        return knownSlices
            .OrderByDescending(candidate => candidate.Length)
            .FirstOrDefault(candidate => consumerPath.StartsWith(candidate + "/", StringComparison.Ordinal))
            ?? consumerPath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static string CrossApiPath(string layer, string consumer)
        => layer == "Domain" ? $"X/{consumer}" : $"Contracts/X/{consumer}";

    private static List<Finding> Summarize(IReadOnlyList<DependencyViolation> violations)
    {
        var groups = violations
            .GroupBy(violation => violation.Group)
            .Select(group =>
            {
                var first = group.First().Finding;
                var remaining = group.Count() - 1;
                var subject = group.Key.Kind is "outside-layer" or "outside-slice" ? "files" : "file pairs";
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

    private static SliceMap InspectZone(
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
        InspectMirrors(zone, entries, sliceMap, findings);
        maps.Add($"architecture map: zone {Display(zone)} · layers [{string.Join(", ", presentLayers)}] "
            + $"· slices [{string.Join(", ", sliceMap.Slices)}]"
            + (sliceMap.Nested.Count == 0
                ? string.Empty
                : $" · nested [{string.Join(", ", sliceMap.Nested)}]"));
        return sliceMap;
    }

    private static SliceMap DiscoverSlices(IReadOnlyList<string> entries)
    {
        const string prefix = "Application/Features/";
        var featurePaths = entries
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => new SliceEntry(
                path[prefix.Length..].Split('/'),
                IsPlaceholder(path)))
            .Where(entry => entry.Parts.Length >= 2)
            .ToList();
        var slices = new HashSet<string>(StringComparer.Ordinal);
        var nestedPaths = new HashSet<string>(StringComparer.Ordinal);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        var emptyGroups = new HashSet<string>(StringComparer.Ordinal);

        foreach (var feature in featurePaths.GroupBy(entry => entry.Parts[0], StringComparer.Ordinal))
        {
            if (feature.Any(entry => (!entry.IsPlaceholder && entry.Parts.Length == 2)
                || entry.Parts[1] == "Contracts"))
            {
                slices.Add(feature.Key);
                foreach (var child in feature.Where(entry => entry.Parts.Length >= 3).Select(entry => entry.Parts[1]))
                {
                    nestedPaths.Add($"{feature.Key}/{child}");
                }

                continue;
            }

            groups.Add(feature.Key);
            var children = feature
                .Where(entry => entry.Parts.Length >= 3)
                .GroupBy(entry => entry.Parts[1], StringComparer.Ordinal)
                .ToList();
            if (children.Count == 0)
            {
                emptyGroups.Add(feature.Key);
            }
            foreach (var child in children)
            {
                slices.Add($"{feature.Key}/{child.Key}");
                foreach (var nested in child.Where(entry => entry.Parts.Length >= 4).Select(entry => entry.Parts[2]))
                {
                    nestedPaths.Add($"{feature.Key}/{child.Key}/{nested}");
                }
            }
        }

        return new SliceMap(
            slices.Order(StringComparer.Ordinal).ToList(),
            nestedPaths.Order(StringComparer.Ordinal).ToList(),
            groups.Order(StringComparer.Ordinal).ToList(),
            emptyGroups.Order(StringComparer.Ordinal).ToList());
    }

    private static void InspectMirrors(
        string zone,
        IReadOnlyList<string> entries,
        SliceMap map,
        List<Finding> findings)
    {
        foreach (var group in map.EmptyGroups)
        {
            findings.Add(Block(
                At(zone, $"Application/Features/{group}"),
                $"empty-slice-or-group: name '{group}', dimension 'Application', expected a non-placeholder file "
                + $"for slice 'Application/Features/{group}/' or at least one slice under "
                + $"'Application/Features/{group}/<Slice>/'"));
        }

        var rootsByLayer = MirrorLayers.ToDictionary(
            layer => layer,
            layer => DiscoverMirrorRoots(layer, entries, map),
            StringComparer.Ordinal);

        foreach (var slice in map.Slices)
        {
            var applicationPath = $"Application/Features/{slice}/";
            if (!HasContent(entries, applicationPath))
            {
                findings.Add(Block(
                    At(zone, applicationPath.TrimEnd('/')),
                    $"empty-slice: slice '{slice}', dimension 'Application', expected non-placeholder content "
                    + $"under '{applicationPath}'"));
            }

            if (!rootsByLayer["Api"].Contains(slice, StringComparer.Ordinal)
                && !rootsByLayer["Consumers"].Contains(slice, StringComparer.Ordinal))
            {
                findings.Add(Block(
                    At(zone, applicationPath.TrimEnd('/')),
                    $"slice-mirror-missing: slice '{slice}', dimension 'input', expected 'Api/Features/{slice}/' "
                    + $"or 'Consumers/Features/{slice}/'"));
            }
        }

        foreach (var (layer, roots) in rootsByLayer)
        {
            foreach (var slice in roots)
            {
                var mirrorPath = MirrorPath(layer, slice);
                if (map.Groups.Contains(slice, StringComparer.Ordinal))
                {
                    var expectedSlice = map.Slices.FirstOrDefault(candidate =>
                        candidate.StartsWith(slice + "/", StringComparison.Ordinal));
                    var expectedPath = expectedSlice is null
                        ? $"{mirrorPath}<Slice>/"
                        : MirrorPath(layer, expectedSlice);
                    findings.Add(Block(
                        At(zone, mirrorPath.TrimEnd('/')),
                        $"file-in-slice-group: group '{slice}', dimension '{layer}', expected files under a group "
                        + $"slice such as '{expectedPath}'"));
                    continue;
                }

                if (!map.Slices.Contains(slice, StringComparer.Ordinal))
                {
                    findings.Add(Block(
                        At(zone, mirrorPath.TrimEnd('/')),
                        $"orphan-slice-mirror: slice '{slice}', dimension '{layer}', expected "
                        + $"'Application/Features/{slice}/'"));
                    continue;
                }

                if (!HasContent(entries, mirrorPath))
                {
                    findings.Add(Block(
                        At(zone, mirrorPath.TrimEnd('/')),
                        $"empty-slice-mirror: slice '{slice}', dimension '{layer}', expected non-placeholder content "
                        + $"under '{mirrorPath}'"));
                }
            }
        }
    }

    private static List<string> DiscoverMirrorRoots(string layer, IReadOnlyList<string> entries, SliceMap map)
    {
        var prefix = layer == "Domain" ? "Domain/" : $"{layer}/Features/";
        return entries
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..])
            .Where(path => path.Contains('/'))
            .Select(path => MirrorSlice(path, map))
            .Where(slice => slice is not null && !(layer == "Domain" && slice == "Shared"))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string? MirrorSlice(string path, SliceMap map)
    {
        var known = map.Slices
            .OrderByDescending(slice => slice.Length)
            .FirstOrDefault(slice => path.StartsWith(slice + "/", StringComparison.Ordinal));
        if (known is not null)
        {
            return known;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        return map.Groups.Contains(parts[0], StringComparer.Ordinal) && parts.Length >= 3
            ? $"{parts[0]}/{parts[1]}"
            : parts[0];
    }

    private static string MirrorPath(string layer, string slice)
        => layer == "Domain" ? $"Domain/{slice}/" : $"{layer}/Features/{slice}/";

    private static bool HasContent(IReadOnlyList<string> entries, string prefix)
        => entries.Any(path => path.StartsWith(prefix, StringComparison.Ordinal) && !IsPlaceholder(path));

    private static bool IsPlaceholder(string path)
        => PlaceholderFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal);

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

    private static Finding Advice(string location, string message)
        => new(FindingSeverity.Advisory, location, message);

    private sealed record SliceMap(
        List<string> Slices,
        List<string> Nested,
        List<string> Groups,
        List<string> EmptyGroups);

    private sealed record SliceEntry(string[] Parts, bool IsPlaceholder);

    private sealed record SliceAddress(string Name, string Path);

    private sealed record LayerAddress(string? Zone, string? Layer, string? RelativePath);

    private sealed record DependencyGroup(
        string Kind,
        string FromZone,
        string? FromLayer,
        string? FromSlice,
        string? ToZone,
        string? ToLayer,
        string? ToSlice);

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
