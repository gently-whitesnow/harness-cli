using Harness.Config;
using Harness.Languages;
using Harness.Repository;
using Harness.Structure;

namespace Harness.Checks.Architecture;

internal sealed class SlicedDotNetShapeCheck(ILanguageAnalyzer analyzer, string rule) : IRepositoryCheck
{
    private const int ShownDependencyGroups = 5;

    // Mirrors the generated-suffix judgement of the C# source reader.

    private static readonly string[] Layers = ArchitectureZones.Layers;

    private static readonly string[] PlaceholderFiles = [".gitkeep", ".keep", ".gitignore"];

    // ADR-0041: the layer = assembly invariant reads tracked C# project XML by name.
    private static readonly EvidenceFile ProjectEvidence = new("*.csproj");

    private static readonly string[] MirrorLayers = ["Api", "Consumers", "Infrastructure", "Domain"];

    private static readonly string[] SliceDimensions = ["Application", .. MirrorLayers];

    private static readonly string[] SlicelessSegmentLayers = ["Host"];

    // ADR-0051: the only two directories in the root of a sliced layer that are neither
    // slices nor mirrors.
    private static readonly string[] ReservedDirectories = ["Domain/Shared", "Infrastructure/Persistence"];

    // The public API of a slice; at the root of a sliced layer it is the no-layer-public-api
    // finding rather than a slice, group or mirror.
    private const string PublicApiDirectory = "Contracts";

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
            ["Api"] = ["Application", "Domain"],
            ["Consumers"] = ["Application", "Domain"],
            ["Application"] = ["Domain"],
            ["Domain"] = [],
            ["Infrastructure"] = ["Application", "Domain"],
        };

    public const string Family = "architecture.sliced-dotnet";

    public static readonly IReadOnlyDictionary<string, string> Rules = new Dictionary<string, string>
    {
        ["zone-shape"] = "canonical zones, required layers and source placement",
        ["slice-shape"] = "slice groups, input mirrors and slice placement",
        ["segment-names"] = "declared segment and slice naming vocabulary",
        ["layer-assemblies"] = "one project per layer, owned compilation and project references",
        ["dependency-direction"] = "source dependencies follow the layer DAG and zone boundary",
        ["slice-isolation"] = "same-layer slices communicate through explicit cross-API",
        ["public-api"] = "Application slice boundaries use Contracts; layers publish no API",
        ["cross-api"] = "X contracts are imported only by their named consumer or Host",
    };

    public string Id => $"{Family}.{rule}";

    public string Group => Family;

    public IReadOnlyList<EvidenceFile> Evidence => rule == "layer-assemblies" ? [ProjectEvidence] : [];

    public string Summary => Rules[rule];

    public string Explanation => $"{Id}: {Summary}\n\n" +
        """
        Rationale
          The sliced-dotnet/1 standard makes the tracked directory tree the architecture map.
          Every application uses the same layer vocabulary, so a review does not depend on a
          repository-specific declaration. This is tier 1 of the contract 2.0 model: immutable
          topology invariants. Tier 2 is the DSM held under the declared settings limits;
          tier 3 is explicit repository policy. ADR-0032 defines the tiers, ADR-0033 defines sliced-dotnet and ADR-0051 puts
          the slices of sliced-dotnet/1 directly in the layer root.

        What it reads
          The family reads tracked paths and the non-generated C# dependency graph.
          Only layer-assemblies reads tracked *.csproj XML, without MSBuild evaluation.
          Segment-names reads paths alone. The other checks share one source graph.
          A directory containing Application/ starts one zone. The family discovers
          canonical layer directories and the slices directly below Application/, including one
          optional grouping level. Dependency edges are lexical evidence: only Proven edges can fail this
          fitness function; Inferred edges are ignored. The graph is shared within one run.

        What it accepts
          Every zone contains Host, Application and at least one of Api or Consumers. Every
          present layer contains a tracked file other than .gitkeep, .keep or .gitignore, and
          every directory directly below a zone is a canonical layer. The architecture map is
          printed with --verbose, including nested paths that would otherwise disappear.

          The layer DAG is an invariant, not a score:
            Host           -> every layer
            Api            -> Application, Domain
            Consumers      -> Application, Domain
            Application    -> Domain
            Domain         -> no other layer
            Infrastructure -> Application/Contracts, Domain
          References inside one layer are allowed. References between architecture zones are
          never allowed. This layer-pair stage accepts Infrastructure -> Application; the public
          API invariant separately restricts its destination to Contracts/. It also accepts all
          Infrastructure -> Domain edges because Domain is the common vocabulary available to
          every upper layer.

          The layer is also the assembly. When the repository tracks SDK-style C# projects at
          all, every canonical layer holding C# sources is exactly one .csproj, a project
          compiles only its own layer — a literal Compile Include reaching another layer is
          linked compilation and fails — and ProjectReference edges between the zone's
          projects follow the same layer DAG as the source graph. A reference into another
          zone is never allowed. Item paths carrying MSBuild properties are not evaluated and
          not judged. Without any tracked SDK-style project there is no build layout to hold
          to the standard, and these invariants stay silent.

          Slices inside one layer do not reference each other directly, including through an
          ordinary Contracts/ directory. A producer can expose a consumer-specific cross-API at
          Contracts/X/<Consumer>/ (or Domain/<Producer>/X/<Consumer>/); only that named consumer
          and Host composition may import it. Every reference into an Application slice from
          outside that slice goes through Contracts/, except Host composition. Upper layers may
          freely compose different Domain slices. Slice isolation is evaluated within one layer:
          a mirror may consume the ordinary public Contracts/ of a differently named Application
          slice.

          Slices sit directly in the root of a sliced layer — Application, Api, Consumers,
          Infrastructure and Domain — the way Feature-Sliced Design puts slices in a layer: a file
          directly inside <Layer>/<Name>/ makes <Name> a slice. Without such a file, that
          directory is a group and its child directories are the slices. Host stays sliceless.

          Application is the source of slice names. Every slice has a non-empty mirror in
          Api/<Slice> or Consumers/<Slice>. Api, Consumers, Infrastructure and Domain mirrors
          cannot introduce a slice that Application does not contain: a directory in the root of
          a sliced layer is a slice mirror, and cross-cutting code belongs in Host or in a slice
          (orphan-slice-mirror). Only Domain/Shared and Infrastructure/Persistence are reserved
          non-slice directories. Placeholder-only slices, groups and mirrors are empty
          architecture forms and fail the check.

          A Contracts/ directory directly below a sliced layer is no-layer-public-api: a slice
          publishes its own Contracts/, the layer has none. That directory is neither a slice, a
          group nor a mirror, and it produces no second finding.

          Direct segments in slices and in the sliceless Host layer are named by purpose
          rather than by the kind of code they contain. The segments-by-purpose finding literally
          ports BAD_NAMES_GENERIC from the Steiger commit pinned by ADR-0037. The five backend
          additions — Common, Manager, Managers, Repository and Repositories — belong to the
          sliced-dotnet/1 policy. Steiger's frontend framework vocabularies are intentionally not
          part of this .NET standard.
          An essence-based leaf below a slice group, and an essence-named directory in the root of
          a mirror layer, are rejected as segments on a sliced dimension, following Steiger's
          no-segments-on-sliced-layers rule. These are explicit vocabulary rules; no naming,
          size or cross-API density heuristic is reported as an architectural problem.

        Policy
          A violation is blocking when the tracked policy for this check is required, and no
          path, file or finding is exempt from it. A repository that has not moved to the
          standard yet may run each individual check `advisory` or `off`; that broader decision is one
          reviewable line in the tracked frame and the report states it on every run.

        Remediation
          Move application files under Host, Api, Consumers, Application, Domain or
          Infrastructure. Put use-case slices in Application/<Slice>, or group them one level
          deeper as Application/<Group>/<Slice> without files in the group directory. Move
          a legacy <Layer>/Features/<Slice> to <Layer>/<Slice>, move cross-cutting
          web plumbing from the root of a sliced layer into Host or into a slice, and move a
          layer-level Contracts/ into the slice that owns it.
          Turn a forbidden dependency around or move the shared concept to an allowed lower layer.
          Give every layer that holds C# sources its own project, replace a Compile Include of
          another layer's sources with a ProjectReference to that layer's project, and remove a
          ProjectReference the layer table does not allow.
          For a cross-slice dependency, prefer merging slices, then moving the shared concept down
          to Domain, and use an explicit X/<Consumer> cross-API only as a last resort.
          Give every Application slice a synchronous or asynchronous input mirror, remove orphaned
          mirrors, and replace placeholder-only architecture directories with working code or remove
          the dead form. Rename an essence-based segment such as Services, Validators or Repositories
          after the purpose it serves. If the name is a leaf below a slice group, either choose a
          business slice name or move the segment inside a named slice. These naming checks expose
          structural ambiguity; they do not prove semantic slice cohesion.
          Inspect the named files behind every reported Proven violation.
          Select all architecture checks with architecture or architecture.sliced-dotnet;
          policy accepts only the individual IDs, never an aggregate switch.

        Decisions
          adrs/0032-topology-over-thresholds.md
          adrs/0033-canonical-standard-over-declarations.md
          adrs/0036-input-layers-read-domain.md
          adrs/0037-segments-by-purpose.md
          adrs/0041-layer-is-the-assembly.md
          adrs/0051-slices-in-the-layer-root.md
          adrs/0053-explicit-architecture-checks.md
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
            .Where(entry => context.Repository.Classify(entry) is not (EvidenceKind.DeclaredGenerated or EvidenceKind.ToolchainIgnored))
            .Select(entry => entry.Path)
            .ToList();
        var zones = ArchitectureZones.Discover(paths);
        if (zones.Count == 0)
        {
            return CheckEvaluation.From(
                rule == "zone-shape"
                    ? [Block(".", "no architecture zone found; sliced-dotnet/1 requires a directory containing Application/")]
                    : [],
                details: ["architecture map: no zones"]);
        }

        var shapeFindings = new List<Finding>();
        var sliceFindings = new List<Finding>();
        var apiFindings = new List<Finding>();
        var findings = new List<Finding>();
        var maps = new List<string>();
        var slicesByZone = new Dictionary<string, SliceMap>(StringComparer.Ordinal);
        foreach (var zone in zones)
        {
            var map = InspectZone(zone, paths, shapeFindings, maps);
            slicesByZone[zone] = map;
            InspectMirrors(zone, paths.Select(path => Relative(path, zone)).ToList(), map, sliceFindings);
            InspectLayerPublicApi(zone, paths.Select(path => Relative(path, zone)).ToList(), apiFindings);
            if (rule == "segment-names")
            {
                InspectSegmentPurposes(zone, map, paths, findings);
            }
        }

        if (rule == "layer-assemblies")
        {
            var (projects, projectFailure) = ReadProjects(context);
            if (projectFailure is not null)
            {
                return CheckEvaluation.Incomplete(projectFailure, details: maps);
            }
            foreach (var zone in zones)
            {
                InspectLayerProjects(zone, zones, paths, projects, findings);
            }
            return CheckEvaluation.From(findings, details: maps);
        }
        if (rule == "segment-names")
        {
            return CheckEvaluation.From(findings, details: maps);
        }

        findings.AddRange(rule switch
        {
            "zone-shape" => shapeFindings,
            "slice-shape" => sliceFindings,
            "public-api" => apiFindings,
            _ => [],
        });
        var (graph, failure) = analyzer.ReadGraph(context.Repository);
        if (graph is null)
        {
            return CheckEvaluation.Incomplete(failure!, findings, maps);
        }
        var dependencies = InspectDependencies(
            zones,
            slicesByZone.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Slices),
            graph).Where(dependency => Owns(dependency.Group.Kind)).ToList();
        return CheckEvaluation.From(
            [.. findings, .. Summarize(dependencies)],
            detailedFindings: [.. findings, .. dependencies.Select(dependency => dependency.Finding)],
            details: maps);
    }

    private bool Owns(string kind) => rule switch
    {
        "zone-shape" => kind == "outside-layer",
        "slice-shape" => kind == "outside-slice",
        "dependency-direction" => kind is "cross-zone" or "layer-pair",
        "slice-isolation" => kind == "slice-pair",
        "public-api" => kind == "application-public-api",
        "cross-api" => kind == "cross-api-consumer",
        _ => false,
    };

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
                findings.Add(Block(
                    At(zone, $"Application/{slice}"),
                    $"no-segments-on-sliced-layers: application slice '{slice}' ends in essence-based "
                    + $"name '{leaf}'; choose a business slice name or move that segment inside a named slice"));
            }

            foreach (var dimension in SliceDimensions)
            {
                var slicePrefix = $"{dimension}/{slice}/";
                foreach (var segment in entries
                    .Where(path => path.StartsWith(slicePrefix, StringComparison.Ordinal))
                    .Select(path => ImmediateDirectory(path[slicePrefix.Length..]))
                    .Where(segment => segment is not null && IsEssenceBasedSegmentName(segment))
                    .Select(segment => segment!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(Block(
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
                findings.Add(Block(
                    At(zone, $"{layerPrefix}{segment}"),
                    $"segments-by-purpose: sliceless layer '{layer}', segment '{segment}' names what its "
                    + "contents are; rename it after the purpose those contents serve"));
            }
        }
    }

    private static (IReadOnlyList<DotNetFile> Projects, string? Failure) ReadProjects(CheckContext context)
    {
        var projects = new List<DotNetFile>();
        foreach (var entry in context.Tracked(ProjectEvidence))
        {
            var (file, failure) = DotNetRepository.Read(context.Repository, entry);
            if (failure is not null)
            {
                return ([], failure);
            }

            if (DotNetRepository.IsSdkStyle(file!))
            {
                projects.Add(file!);
            }
        }

        return (projects, null);
    }

    /// <summary>
    /// ADR-0041: layer = assembly. Every canonical layer holding C# sources is exactly one
    /// project, a project compiles only its own layer, and project references between the
    /// zone's projects follow the same layer DAG the source graph is held to. Judged only
    /// when the repository tracks SDK-style C# projects at all: without them there is no
    /// build layout to hold to the standard.
    /// </summary>
    private static void InspectLayerProjects(
        string zone,
        IReadOnlyList<string> zones,
        IReadOnlyList<string> paths,
        IReadOnlyList<DotNetFile> projects,
        List<Finding> findings)
    {
        if (projects.Count == 0)
        {
            return;
        }

        var entries = paths
            .Select(path => Relative(path, zone))
            .Where(path => path.Length > 0 && !path.StartsWith("../", StringComparison.Ordinal))
            .ToList();
        var byLayer = new Dictionary<string, List<DotNetFile>>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            var relative = Relative(project.Path, zone);
            if (relative.StartsWith("../", StringComparison.Ordinal))
            {
                continue;
            }

            var layer = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (layer is null || !Layers.Contains(layer, StringComparer.Ordinal))
            {
                findings.Add(Block(project.Path,
                    $"project-outside-layer: project '{project.Path}' is inside architecture zone but outside "
                    + "every canonical layer; sliced-dotnet/1 builds every layer as its own project"));
                continue;
            }

            if (!byLayer.TryGetValue(layer, out var owned))
            {
                byLayer[layer] = owned = [];
            }

            owned.Add(project);
        }

        foreach (var layer in Layers)
        {
            var layerProjects = byLayer.TryGetValue(layer, out var owned) ? owned : [];
            if (layerProjects.Count == 0
                && entries.Any(path => path.StartsWith(layer + "/", StringComparison.Ordinal)
                    && IsAuthoredSource(path)))
            {
                findings.Add(Block(At(zone, layer),
                    $"layer-project-missing: layer '{layer}' contains C# sources but no tracked .csproj; "
                    + "sliced-dotnet/1 builds every layer as its own project"));
            }

            if (layerProjects.Count > 1)
            {
                findings.Add(Block(At(zone, layer),
                    $"layer-project-count: layer '{layer}' contains {layerProjects.Count} projects "
                    + $"[{string.Join(", ", layerProjects.Select(project => project.Path))}]; "
                    + "sliced-dotnet/1 builds every layer as exactly one project"));
            }

            foreach (var project in layerProjects)
            {
                InspectCompiledSources(zone, layer, project, findings);
                InspectProjectReferences(zone, zones, layer, project, findings);
            }
        }
    }

    private static void InspectCompiledSources(
        string zone,
        string layer,
        DotNetFile project,
        List<Finding> findings)
    {
        var layerPrefix = At(zone, layer) + "/";
        foreach (var include in ItemPaths(project, "Compile"))
        {
            var resolved = DotNetRepository.NormalizeRelative(project.Path, include);
            if (!resolved.StartsWith(layerPrefix, StringComparison.Ordinal))
            {
                findings.Add(Block(project.Path,
                    $"linked-compilation: Compile Include '{include}' reaches outside layer '{layer}'; "
                    + "every layer compiles only its own sources — reference the owning layer's project instead"));
            }
        }
    }

    private static void InspectProjectReferences(
        string zone,
        IReadOnlyList<string> zones,
        string layer,
        DotNetFile project,
        List<Finding> findings)
    {
        foreach (var reference in ItemPaths(project, "ProjectReference"))
        {
            var resolved = DotNetRepository.NormalizeRelative(project.Path, reference);
            var target = Address(resolved, zones);
            if (target.Zone is null)
            {
                continue;
            }

            if (!string.Equals(target.Zone, zone, StringComparison.Ordinal))
            {
                findings.Add(Block(project.Path,
                    $"cross-zone project reference is forbidden: {layer} project '{project.Path}' references "
                    + $"'{resolved}' in zone '{Display(target.Zone)}'"));
                continue;
            }

            if (target.Layer is not null
                && target.Layer != layer
                && !AllowedDependencies[layer].Contains(target.Layer))
            {
                findings.Add(Block(project.Path,
                    $"layer project reference {layer} -> {target.Layer} is forbidden by sliced-dotnet/1: "
                    + $"'{project.Path}' references '{resolved}'"));
            }
        }
    }

    /// <summary>
    /// Literal item paths of one MSBuild item name. A value carrying an MSBuild property is
    /// not evaluated: ADR-0019 keeps the harness out of MSBuild evaluation, so only literal
    /// paths are judged.
    /// </summary>
    private static IEnumerable<string> ItemPaths(DotNetFile project, string itemName)
        => DotNetRepository.Elements(project, itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .SelectMany(value => value.Split(
                ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !value.Contains("$(", StringComparison.Ordinal));

    private static bool IsAuthoredSource(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsEssenceBasedSegmentName(string name)
        => SteigerBadNamesGeneric.Contains(name)
            || BackendEssenceBasedSegmentNames.Contains(name);

    private static string? ImmediateDirectory(string relativePath)
    {
        var separator = relativePath.IndexOf('/');
        return separator > 0 ? relativePath[..separator] : null;
    }

    private List<DependencyViolation> InspectDependencies(
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

    private DependencyViolation? SliceViolation(
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
        if (rule == "cross-api" && crossConsumer is not null
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

        if (rule == "slice-isolation" && from.Layer == to.Layer
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

        if (rule == "public-api" && to.Layer == "Application"
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
        var prefix = SliceRootPrefix(address.Layer);
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
        if (slice is null || IsReserved(address.Layer!, slice) || IsLayerPublicApi(slice))
        {
            return null;
        }

        return new SliceAddress(slice, inside[(slice.Length + 1)..]);
    }

    private static bool IsDirectlyInsideSliceRoot(LayerAddress address)
    {
        var prefix = SliceRootPrefix(address.Layer);
        return prefix is not null
            && address.RelativePath!.StartsWith(prefix, StringComparison.Ordinal)
            && !address.RelativePath[prefix.Length..].Contains('/');
    }

    private static string? SliceRootPrefix(string? layer)
        => layer is not null && SliceDimensions.Contains(layer, StringComparer.Ordinal)
            ? $"{layer}/"
            : null;

    /// <summary>ADR-0051: Domain/Shared and Infrastructure/Persistence are neither slices nor mirrors.</summary>
    private static bool IsReserved(string layer, string directory)
        => ReservedDirectories.Contains($"{layer}/{directory}", StringComparer.Ordinal);

    /// <summary>A Contracts/ directory directly below a sliced layer, judged by no-layer-public-api.</summary>
    private static bool IsLayerPublicApi(string directory)
        => directory.Equals(PublicApiDirectory, StringComparison.Ordinal)
            || directory.StartsWith(PublicApiDirectory + "/", StringComparison.Ordinal);

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
        maps.Add($"architecture map: zone {Display(zone)} · layers [{string.Join(", ", presentLayers)}] "
            + $"· slices [{string.Join(", ", sliceMap.Slices)}]"
            + (sliceMap.Nested.Count == 0
                ? string.Empty
                : $" · nested [{string.Join(", ", sliceMap.Nested)}]"));
        return sliceMap;
    }

    /// <summary>
    /// ADR-0051: port of Steiger's no-layer-public-api. A slice publishes its own Contracts/;
    /// a Contracts/ directory directly below a sliced layer is neither a slice, a group nor a
    /// mirror, so the other slice rules skip it and this is its only finding.
    /// </summary>
    private static void InspectLayerPublicApi(
        string zone,
        IReadOnlyList<string> entries,
        List<Finding> findings)
    {
        foreach (var layer in SliceDimensions)
        {
            var prefix = $"{layer}/{PublicApiDirectory}/";
            if (entries.Any(path => path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                findings.Add(Block(
                    At(zone, prefix.TrimEnd('/')),
                    $"no-layer-public-api: layer '{layer}' publishes {PublicApiDirectory}/ at its root; a slice "
                    + $"publishes its own {PublicApiDirectory}/ — move them into {layer}/<Slice>/{PublicApiDirectory}/"));
            }
        }
    }

    private static SliceMap DiscoverSlices(IReadOnlyList<string> entries)
    {
        const string prefix = "Application/";
        var featurePaths = entries
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => new SliceEntry(
                path[prefix.Length..].Split('/'),
                IsPlaceholder(path)))
            .Where(entry => entry.Parts.Length >= 2 && !IsLayerPublicApi(entry.Parts[0]))
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
                At(zone, $"Application/{group}"),
                $"empty-slice-or-group: name '{group}', dimension 'Application', expected a non-placeholder file "
                + $"for slice 'Application/{group}/' or at least one slice under "
                + $"'Application/{group}/<Slice>/'"));
        }

        var rootsByLayer = MirrorLayers.ToDictionary(
            layer => layer,
            layer => DiscoverMirrorRoots(layer, entries, map),
            StringComparer.Ordinal);

        foreach (var slice in map.Slices)
        {
            var applicationPath = $"Application/{slice}/";
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
                    $"slice-mirror-missing: slice '{slice}', dimension 'input', expected 'Api/{slice}/' "
                    + $"or 'Consumers/{slice}/'"));
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
                    findings.Add(IsEssenceBasedSegmentName(slice)
                        ? Block(
                            At(zone, mirrorPath.TrimEnd('/')),
                            $"no-segments-on-sliced-layers: layer '{layer}' holds segment '{slice}' at its root; "
                            + "a sliced layer holds only slices — move it into Host or into a slice")
                        : Block(
                            At(zone, mirrorPath.TrimEnd('/')),
                            $"orphan-slice-mirror: slice '{slice}', dimension '{layer}', expected "
                            + $"'Application/{slice}/'; a directory in the root of a sliced layer is a slice "
                            + "mirror — move cross-cutting code into Host or into a slice"));
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
        var prefix = $"{layer}/";
        return entries
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..])
            .Where(path => path.Contains('/'))
            .Select(path => MirrorSlice(path, map))
            .Where(slice => slice is not null && !IsReserved(layer, slice) && !IsLayerPublicApi(slice))
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

    private static string MirrorPath(string layer, string slice) => $"{layer}/{slice}/";

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

    private static string Display(string zone) => ArchitectureZones.Display(zone);

    private static string Relative(string path, string zone) => ArchitectureZones.Relative(path, zone);

    private static string At(string zone, string name) => zone.Length == 0 ? name : $"{zone}/{name}";

    private static Finding Block(string location, string message)
        => new(FindingSeverity.Blocking, location, message);

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
