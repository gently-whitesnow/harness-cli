using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Harness.Checks.Architecture;
using Harness.Checks.LintSuppressions;
using Harness.Repository;
using Harness.Versioning;

namespace Harness.Config;

internal static class FrameUpgrade
{
    public static (string? Report, string? Failure) Raise(
        IRepository repository,
        bool dryRun,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var path = Path.Combine(repository.RootPath, HarnessConfig.FileName);
        if (!repository.TrackedEntries.Any(entry => entry.Path == HarnessConfig.FileName))
        {
            return (null, $"'{HarnessConfig.FileName}' must be tracked before it can be upgraded.");
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not read '{path}': {exception.Message}");
        }

        var (pin, failure) = ReadPin(text);
        if (pin is null)
        {
            return (null, failure);
        }

        // The reader refuses an ambiguous frame, so the upgrade names the duplicate first
        // instead of rewriting a pin that `check` would then reject.
        var duplicate = DuplicateProperty(text);
        if (duplicate is not null)
        {
            return (null, ConfigJson.Failure($"duplicate property '{duplicate}' leaves the frame ambiguous; keep one before upgrading"));
        }

        var (migrated, migrationFailure) = SplitArchitecturePolicy(text);
        if (migrationFailure is not null)
        {
            return (null, migrationFailure);
        }
        var policyChanged = migrated != text;
        if (!policyChanged && (pin is "latest" || pin == HarnessVersion.Current.ToString()))
        {
            return ($"{HarnessConfig.FileName} already runs contract {pin}; there is no pin to raise.\n"
                + Additions(repository, migrated!, checks), null);
        }

        if (!HarnessVersion.TryParse(pin == "latest" ? HarnessVersion.Current.ToString() : pin, out var version))
        {
            return (null, $"'version' is not a harness release: {pin}.");
        }

        if (IsNewer(version, HarnessVersion.Current))
        {
            return (null, $"This binary is harness {HarnessVersion.Current} and cannot migrate the newer pin {pin}; update the harness first.");
        }

        var target = pin == "latest" ? pin : HarnessVersion.Current.ToString();
        if (!dryRun)
        {
            var rewriteFailure = Rewrite(path, migrated!, pin, target);
            if (rewriteFailure is not null)
            {
                return (null, rewriteFailure);
            }
        }

        var action = pin == target
            ? dryRun ? "Would update" : "Updated"
            : dryRun ? "Would raise" : "Raised";
        var report = new StringBuilder();
        report.Append($"{action} {HarnessConfig.FileName} from {pin} to {target}.\n");

        // Only the route from the pin: a repository that already lives in 2.16 has read the
        // 2.0 migration before, and the whole history says nothing about what changes for it.
        foreach (var (since, note) in ReleaseNotes)
        {
            if (pin == "latest" ? since == HarnessVersion.Current : IsNewer(since, version))
            {
                report.Append(note).Append('\n');
            }
        }

        report.Append(Additions(repository, migrated!, checks));
        report.Append("Review these sections, then run `harness check --verbose`. ")
            .Append(dryRun
                ? "Nothing was written."
                : policyChanged
                    ? "The pin and architecture policy were migrated; repository answers were not guessed."
                    : "Only the pin was changed; repository answers were not guessed.")
            .Append('\n');
        return (report.ToString(), null);
    }

    /// <summary>
    /// The sections this repository is missing for the stacks its index shows: an axis with
    /// tracked sources but no applicability, and every check of a declared axis — or of no
    /// axis — that the policy does not name yet. Printed, never written: the owner chooses.
    /// </summary>
    private static string Additions(IRepository repository, string text, IReadOnlyList<CheckDescriptor> checks)
    {
        using var document = JsonDocument.Parse(text, ConfigJson.ParseOptions);
        var (projects, failure) = WorkspaceProjects.Read(document.RootElement);
        if (projects is null)
        {
            return $"Workspace registration needs review: {failure}.\n";
        }

        // A nested config nobody registered stops `check` outright, so the upgrade names each
        // one before the owner decides whether it is a project frame or a leftover.
        var unregistered = WorkspaceScope.Unregistered(repository, projects);
        var review = unregistered.Count == 0
            ? string.Empty
            : "Tracked nested configs that root 'projects' does not register stop verification; "
                + "register each directory or remove the file:\n  "
                + string.Join("\n  ", unregistered) + "\n";

        if (projects.Count == 0)
        {
            return review + ScopeAdditions(repository, text, checks);
        }

        var report = new StringBuilder(review).Append("Workspace root (.harness.json):\n");
        report.Append(ScopeAdditions(WorkspaceScope.RootScope(repository, projects), text, checks));
        var projectChecks = checks.Where(check => WorkspaceScope.IsProjectCheck(check.Id)).ToList();
        foreach (var project in projects)
        {
            report.Append($"Project {project} ({WorkspaceScope.ConfigPath(project)}):\n");
            var scope = new ScopedRepository(repository, project);
            var entry = scope.TrackedEntries.FirstOrDefault(candidate => candidate.Path == HarnessConfig.FileName);
            if (entry is null)
            {
                report.Append("  Configuration is not tracked; register a complete project frame.\n");
                continue;
            }

            var (local, readFailure) = scope.ReadTrackedText(entry);
            if (local is null)
            {
                report.Append("  ").Append(readFailure).Append('\n');
                continue;
            }

            try
            {
                report.Append(ScopeAdditions(scope, local, projectChecks));
            }
            catch (JsonException exception)
            {
                report.Append("  Configuration needs review: ").Append(exception.Message).Append('\n');
            }
        }

        report.Append("Project frames keep local answers and policies; only the root pin is upgraded.\n");
        return report.ToString();
    }

    private static string ScopeAdditions(IRepository repository, string text, IReadOnlyList<CheckDescriptor> checks)
    {
        var document = JsonNode.Parse(text, documentOptions: ConfigJson.ParseOptions) as JsonObject;
        var policy = document?["policy"] as JsonObject ?? [];
        var applicability = document?["applicability"] as JsonObject ?? [];
        var detected = FrameAxis.Detected(repository);
        var report = new StringBuilder();
        foreach (var axis in detected.Where(axis => !applicability.ContainsKey(axis.Key)))
        {
            report.Append($"Sections to add for tracked {axis.Name} sources (or decline the axis with a reason):\n")
                .Append(FrameSections.Indent(FrameSections.AxisFragment(axis, checks, document?["architecture"] is null), "  "))
                .Append('\n');
        }

        var declared = detected.Where(axis => applicability.ContainsKey(axis.Key)).Select(axis => axis.Key).ToHashSet(StringComparer.Ordinal);
        if (declared.Contains("csharp") && applicability["csharp"] is JsonObject csharp
            && csharp["applicable"]?.GetValueKind() == JsonValueKind.True
            && document?["architecture"] is null)
        {
            report.Append(FrameSections.ArchitectureFragment(checks)).Append('\n');
        }

        var missing = checks
            .Where(check => !policy.ContainsKey(check.Id))
            .Where(check => check.Applicability is null ? check.Group != "architecture.sliced-dotnet" || document?["architecture"] is not null : declared.Contains(check.Applicability))
            .ToList();
        if (missing.Count > 0)
        {
            var settings = missing.Select(check => FrameSections.DefaultSettings(check)).Where(section => section is not null).ToList();
            report.Append("Checks this release ships that the policy does not name yet (outside the frame until named):\n");
            if (settings.Count > 0)
            {
                report.Append("  \"settings\": {\n")
                    .Append(FrameSections.Indent(string.Join(",\n", settings!), "    "))
                    .Append("\n  }\n");
            }

            report.Append("  \"policy\": {\n")
                .Append(string.Join(",\n", missing.Select(check => "    " + FrameSections.PolicyEntry(check))))
                .Append("\n  }\n");
        }

        report.Append(WeakeningMigration(repository, document));

        return report.ToString();
    }

    private static string WeakeningMigration(IRepository repository, JsonObject? document)
    {
        var report = new StringBuilder();
        var declared = document?["generated"] as JsonArray;
        var named = declared?.OfType<JsonObject>()
            .SelectMany(entry => (entry["paths"] as JsonArray)?.OfType<JsonValue>()
                .Select(value => value.GetValue<string>()) ?? [])
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var generated = repository.TrackedEntries
            .Where(entry => repository.Classify(entry) == EvidenceKind.UndeclaredMarker)
            .Select(entry => entry.Path)
            .Where(path => !named.Any(prefix => path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal)))
            .Select(path => path.LastIndexOf('/') is var slash && slash > 0 ? path[..slash] : path)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (generated.Count > 0)
        {
            report.Append("Generated sources needing reviewed declarations (paths may be grouped by directory):\n")
                .Append("  \"generated\": [\n");
            for (var index = 0; index < generated.Count; index++)
            {
                report.Append("    { \"paths\": [\"").Append(generated[index])
                    .Append("\"], \"reason\": \"identify the generator and why output is tracked\" }")
                    .Append(index + 1 < generated.Count ? ",\n" : "\n");
            }

            report.Append("  ]\n");
        }

        var wide = CollectWideSuggestions(repository);

        foreach (var (section, ids) in wide)
        {
            var existing = (document?["settings"]?[section]?["repositoryWide"] as JsonArray)?
                .OfType<JsonObject>().Select(entry => entry["id"]?.GetValue<string>())
                .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var missing = ids.Where(id => !existing.Contains(id)).Order(StringComparer.Ordinal).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            report.Append($"Review repository-wide switches; only genuinely global entries belong in settings.{section}:\n")
                .Append($"  \"{section}\": {{ \"repositoryWide\": [\n");
            for (var index = 0; index < missing.Count; index++)
            {
                report.Append($"    {{ \"id\": \"{missing[index]}\", \"reason\": \"explain why this rule is disabled\" }}")
                    .Append(index + 1 < missing.Count ? ",\n" : "\n");
            }

            report.Append("  ] }\n");
        }

        return report.ToString();
    }

    private static Dictionary<string, HashSet<string>> CollectWideSuggestions(IRepository repository)
    {
        var wide = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in repository.TrackedEntries.Where(entry => entry.Path.EndsWith(".editorconfig", StringComparison.Ordinal)
            || entry.Path.EndsWith("Directory.Build.props", StringComparison.Ordinal)
            || entry.Path.EndsWith(".ansible-lint", StringComparison.Ordinal)
            || entry.Path.EndsWith(".ansible-lint.yml", StringComparison.Ordinal)
            || entry.Path.EndsWith(".ansible-lint.yaml", StringComparison.Ordinal)
            || GolangciConfig.FileNames.Any(name => entry.Path.EndsWith(name, StringComparison.Ordinal))))
        {
            var (text, _) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                continue;
            }

            if (entry.Path.EndsWith(".editorconfig", StringComparison.Ordinal))
            {
                foreach (Match match in Regex.Matches(text,
                    @"dotnet_diagnostic\.([A-Za-z]+\d+)\.severity\s*=\s*(?:none|silent|suggestion)", RegexOptions.IgnoreCase))
                {
                    Add("warning-suppressions.dotnet", match.Groups[1].Value.ToUpperInvariant());
                }
            }
            else if (entry.Path.EndsWith("Directory.Build.props", StringComparison.Ordinal))
            {
                foreach (Match match in Regex.Matches(text, @"<NoWarn>(.*?)</NoWarn>", RegexOptions.Singleline))
                {
                    foreach (var id in Regex.Matches(match.Groups[1].Value, @"[A-Za-z]+\d+").Select(found => found.Value.ToUpperInvariant()))
                    {
                        Add("warning-suppressions.dotnet", id);
                    }
                }
            }
            else if (entry.Path.Contains(".ansible-lint", StringComparison.Ordinal))
            {
                var root = YamlConfig.Parse(text);
                foreach (var name in new[] { "skip_list", "warn_list" })
                {
                    foreach (var id in root.Member(name)?.Values ?? [])
                    {
                        Add("lint-suppressions.ansible", id);
                    }
                }
            }
            else
            {
                var (root, _) = GolangciConfig.Parse(entry.Path, text);
                foreach (var id in root?.Get("linters", "disable")?.Values ?? [])
                {
                    Add("lint-suppressions.go", id);
                }
            }

            void Add(string section, string id)
            {
                if (!wide.TryGetValue(section, out var ids))
                {
                    wide[section] = ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                ids.Add(id);
            }
        }

        return wide;
    }

    private static readonly IReadOnlyList<(HarnessVersion Since, string Note)> ReleaseNotes =
    [
        (new HarnessVersion(2, 0, 0), """
        Contract 2.0 migration:
          removed  maintainability.csharp, cohesion.csharp, suppress, overrides and legacy defaults
          added    architecture: { "standard": "sliced-dotnet/1" } or { "applicable": false, "reason": "..." }
          added    explicit applicability, settings and policy entries for every shipped check
          added    tracked .harness.budget.json with complexity.csharp propagationCost and coreSize
                   (retired again in 2.15, see below)
        """),
        (new HarnessVersion(2, 4, 0), """
        Release 2.4 additions:
          added    architecture.sliced-dotnet advisory observations flat-directory-grouping,
                   mutual-cross-api, cross-api-fan-in and directories-by-purpose; they never
                   change the exit code, and no config change is needed
        """),
        (new HarnessVersion(2, 5, 0), """
        Release 2.5 additions:
          added    architecture.sliced-dotnet layer = assembly invariants: every canonical
                   layer holding C# sources is exactly one .csproj, a Compile Include cannot
                   reach another layer, and ProjectReference edges between the zone's
                   projects follow the layer table; blocking for repositories that still
                   compile several layers into one project — split the projects, no config
                   change is needed
        """),
        (new HarnessVersion(2, 6, 0), """
        Release 2.6 changes:
          changed  complexity.csharp measures the product: the files inside the architecture
                   zones of sliced-dotnet/1, so tests and tooling outside a zone no longer
                   enter the DSM; a repository without a zone is still measured whole
          changed  complexity.csharp measures meanReach (files reachable through dependencies on average,
                   itself included) instead of propagationCost; the report still prints
                   propagation cost for comparison with the literature, and names tracked
                   files an <auto-generated> marker keeps out of the graph
        """),
        (new HarnessVersion(2, 7, 0), """
        Release 2.7 additions:
          added    comments.yaml and comments.typescript: the comment density rule now reads
                   tracked YAML and TypeScript/JavaScript through their own lexical readers;
                   declare applicability yaml and typescript, settings comments.yaml and
                   comments.typescript (minimumCommentLines, percentageLimit) and a policy
                   entry for each check
        """),
        (new HarnessVersion(2, 8, 0), """
        Release 2.8 additions:
          added    editorconfig.dotnet requires the tracked .editorconfig chain above every
                   SDK-style project to carry the shared code-style baseline (LF, final
                   newline, 4 spaces, IDE0055/0065/0161/0011/0040/0007 as warnings and the
                   options behind them, Allman braces); `harness explain editorconfig.dotnet`
                   prints the reference file, `harness init` writes it where none exists
          added    warning-suppressions.dotnet blocks address-level silencing: #pragma
                   warning disable, SuppressMessage, NoWarn in a project file and
                   editorconfig severities none/silent/suggestion in a path-scoped
                   section, outside generated code; a rule switched off for the whole
                   repository ([*.cs] in .editorconfig or NoWarn in Directory.Build.props)
                   stays allowed and is printed as an observation on every run
          added    policy entries for both checks are required; no settings section
        """),
        (new HarnessVersion(2, 9, 0), """
        Release 2.9 changes:
          changed  harness init writes duplication.csharp as required with the existing
                   calibrated 30-line / 90-token profile; existing explicit advisory or off
                   policy remains valid and upgrade changes only the version pin
          added    answers.verify names the tracked repository-owned script that runs every
                   applicable quality check, including `harness check`; frame.verify is
                   required by default, while the harness records the path but never runs
                   or inspects the repository toolchain
        """),
        (new HarnessVersion(2, 10, 0), """
        Release 2.10 changes:
          changed  complexity.csharp draws the product boundary without an architecture zone
                   from tracked project files: authored .cs files whose nearest .csproj is a
                   test project (Microsoft.NET.Test.Sdk, xunit, NUnit, MSTest, TUnit,
                   IsTestProject or MSTest.Sdk) leave the DSM, so a monolith or a library is
                   no longer measured together with its test hosts; sliced-dotnet zones are
                   unchanged, and a repository without any test project is still measured
                   whole
        """),
        (new HarnessVersion(2, 11, 0), """
        Release 2.11 changes:
          changed  answers.tests.unit and answers.tests.integration address a test project —
                   its directory or project file — never the test files inside it: a paths
                   entry ending in .cs, .ts, .tsx, .js or another source suffix the harness
                   reads makes the question Incomplete and the report names the directories
                   to list instead; every paths answer holds at most 5 addresses
        """),
        (new HarnessVersion(2, 12, 0), """
        Release 2.12 changes:
          changed  sliced-dotnet/1 no longer names a Shared layer: Domain is the bottom of the
                   layer DAG and references no other layer, and Domain/Shared stays the reserved
                   home of concepts that belong to no slice; a Shared directory directly below a
                   zone is now outside every canonical layer, so move its code into Domain (a
                   slice or Domain/Shared) and fold the Shared project into the Domain project
        """),
        (new HarnessVersion(2, 13, 0), """
        Release 2.13 changes:
          changed  sliced-dotnet/1 puts slices directly in the layer root: Application/<Slice>,
                   Api/<Slice>, Consumers/<Slice>, Infrastructure/<Slice> and Domain/<Slice>, with
                   one optional group level as before; the Features/ directory is gone — move
                   <Layer>/Features/<Slice> to <Layer>/<Slice>; only Domain/Shared and
                   Infrastructure/Persistence stay reserved, every other directory in the root of
                   a sliced layer must be a slice (no-segments-on-sliced-layers,
                   orphan-slice-mirror) — move cross-cutting web plumbing into Host or a slice
          added    no-layer-public-api blocks a Contracts/ directory directly below a sliced
                   layer: a slice publishes its own Contracts/, the layer has none
          added    advisory observations repetitive-naming (every slice sits in one group) and
                   ambiguous-slice-names (a direct segment repeats its slice name); they never
                   change the exit code
          kept     the standard is still named sliced-dotnet/1: the shape is named by the
                   contract pin, not by a version inside the standard name
        """),
        (new HarnessVersion(2, 14, 0), """
        Release 2.14 changes:
          changed  the managed commit-msg hook holds no binary path: it resolves the harness when
                   it runs — the clone-local <git-common-dir>/harness/bin/harness that
                   `install.sh --scope clone` writes, then `harness` on PATH — and refuses the
                   commit, naming both places, when it finds neither; the hook is now the same
                   text in every clone, so commits.setup no longer depends on which binary wrote
                   it or which one runs the check, and `harness setup` rewrites a hook an older
                   release pinned to a fixed path
          added    commits.setup names what differs — the missing managed file, an unmanaged file
                   in its place, the dead path an older hook exec's, the places searched for a
                   binary — and blocks when the resolved harness is not the pinned release; run
                   `harness setup` after installing the pinned release, no config change is needed
        """),
        (new HarnessVersion(2, 15, 0), """
        Release 2.15 changes:
          removed  .harness.budget.json and `harness budget update`: complexity.csharp no longer
                   compares the DSM with a tracked ratchet; a tracked budget file makes the
                   frame Incomplete until you `git rm .harness.budget.json` and commit
          added    settings.complexity.csharp with meanReach (files, at least 1) and coreSize
                   (files) — the ceiling the check compares the DSM with; the contract defaults
                   are 8.0 and 0, the values of sliced-dotnet/1 that `harness init` writes, and
                   like every settings section it is required and reviewed with the frame
          changed  exceeding either value is blocking under required, and the report names the
                   files outside Host whose own reach is largest — cut edges there, or set the
                   check to advisory or off in policy knowingly; there is no update command
        """),
        (new HarnessVersion(2, 16, 0), """
        Release 2.16 changes:
          renamed  settings.complexity.csharp.meanReach to averageReachableFiles and coreSize
                   to largestCyclicGroupSize; rename both keys and preserve their values
          clarified averageReachableFiles counts reachable dependencies including the source;
                   largestCyclicGroupSize counts the largest mutually reachable file group,
                   not a simple cycle length, and is zero without cycles
          kept     the calculations and default limits 8.0 / 0; upgrade changes only the pin,
                   so rename the settings keys manually before running check
        """),
        (new HarnessVersion(2, 17, 0), """
        Release 2.17 changes:
          removed  built-in architecture smells: flat-directory-grouping, mutual-cross-api,
                   cross-api-fan-in, directories-by-purpose, insignificant-slice, excessive-slicing,
                   inconsistent-slice-pluralization, repetitive-naming and ambiguous-slice-names
          split    architecture.sliced-dotnet policy into zone-shape, slice-shape, segment-names,
                   layer-assemblies, dependency-direction, slice-isolation, public-api and cross-api
                   under the architecture.sliced-dotnet prefix; upgrade copies the old value to each
                   individual check and removes the aggregate key; init makes all eight required
          kept     architecture and architecture.sliced-dotnet as group selectors, not policy keys;
                   architecture maps, DSM and repository-wide warning settings as verbose details
          fixed    Incomplete remains Incomplete even when required findings have been proved
        """),
        (new HarnessVersion(3, 0, 0), """
        Contract 3.0 migration (explicit-only frame):
          changed  a check the policy does not name is outside the frame: it does not run and is
                   not a row of the report (the summary counts it under "outside the frame");
                   applicability, settings and architecture are expected exactly for the checks
                   the policy names — a settings section for a check outside the policy, or a
                   policy entry whose applicability axis is undeclared, is Incomplete
          kept     a check the policy names still needs its settings section in full; there are
                   no hidden numeric defaults, and harness.config is always read
          added    harness.coverage: a tracked language or stack with no applicability entry is a
                   blocking finding that prints the fragment `init` would have written for it;
                   decline an axis with { "applicable": false, "reason": "..." }
          changed  harness init detects languages and .NET from the index and writes only their
                   sections; --languages <keys> replaces detection for CI; the repository kind is
                   asked only when C# is in the index, and .editorconfig is written only with .NET
          kept     a complete 2.17 frame is a valid 3.0 frame: nothing has to be removed, and
                   entries for stacks the repository does not have may now be deleted knowingly
        """),
        (new HarnessVersion(3, 1, 0), """
        Release 3.1 additions:
          added    the go axis: comments.go (settings comments.go, 10/8; doc comments of top-level
                   declarations and tool directives are not prose), duplication.go (settings
                   duplication.go, 30/90, over the shared tokenizer with Go keywords),
                   complexity.go (settings complexity.go, the same keys and 8.0 / 0; the node is
                   the package, resolved through tracked go.mod, main packages are the
                   composition root) and lint-suppressions.go (a bare //nolint or one without
                   linters and a reason is blocking; repository-wide switches in a tracked
                   golangci-lint configuration are printed with --verbose)
          kept     go vet and gofmt are answered through the frame: answers.lint names where
                   `go vet ./...` runs, answers.format where `gofmt -l` is checked; there is no
                   editorconfig.go, types-per-file.go, dependencies.go or Go architecture standard
          added    a repository with tracked .go sources and no applicability.go entry gets the
                   harness.coverage fragment; nothing changes for repositories without Go
        """),

        (new HarnessVersion(3, 2, 0), """
        Release 3.2 additions:
          added    ansible axis detected by ansible.cfg, role entry points and playbooks;
                   dependencies.ansible finds
                   role cycles; lint-suppressions.ansible requires reasons for inline noqa
          changed  comments.yaml excludes blocks above keys or list items at any depth,
                   with blank lines allowed, trailing comments and tool directives;
                   all detected checks, including comments and frame questions, start required;
                   advisory or off requires an explicit decision with the repository owner
          added    harness.coverage reports unknown suffix counts in verbose details;
                   init and frame explanations point to the Ansible toolchain
          kept     explicit policy values and docs.policy; no generated-document exceptions
        """),
        (new HarnessVersion(3, 3, 0), """
        Release 3.3 additions:
          added    projects: ["apps/api", "apps/web"] registers disjoint project directories;
                   each needs its own tracked .harness.json with an explicit local frame
          shared   version, settings.commits and commits.setup belong only to the root;
                   project answers, applicability, settings and policy are not inherited
          changed  tracked nested .harness.json files must be registered; review their ownership
                   before upgrading, because unregistered configurations now stop verification
          scope    root checks cover files outside projects; project checks cover their own files;
                   cross-project duplication and dependency graphs are not measured
          added    harness check --project <path> reports a partial run after workspace validation
          changed  a property repeated at any depth of a frame is refused as ambiguous; the
                   last value used to win silently, and upgrade now names the duplicate first
        """),
        (new HarnessVersion(3, 4, 0), """
        Release 3.4 additions:
          relaxed  docs.policy allows DESIGN.md at any depth and the files a forge reads by
                   itself: CHANGELOG, CODE_OF_CONDUCT, CONTRIBUTING, GOVERNANCE, SECURITY and
                   SUPPORT .md plus issue and pull-request templates, only in the root, .github/
                   or docs/ (and .gitlab/ template directories); none of them is measured
          kept     every other tracked Markdown is still a finding; no frame edit is needed,
                   and a policy softened only for these names can return to required
          added    harness guide prints the agent working loop; a consumer AGENTS.md can point
                   to it in one line instead of restating the harness
        """),
        (new HarnessVersion(3, 5, 0), """
        Release 3.5 additions:
          added    functions.csharp and functions.go measure each method, local function,
                   lambda and Go function independently; C# top-level statements count
          declare  policy functions.<language> as required and
                   settings.functions.<language>.ownLines as 80 for each applicable axis
          kept     tests under the same limit; generated source remains excluded
        """),
        (new HarnessVersion(3, 6, 0), """
        Release 3.6 additions:
          added    dependencies.typescript, duplication.typescript and complexity.typescript
          declare  policy for these checks as required; settings.duplication.typescript is
                   30/90, settings.complexity.typescript is 8.0 / 0
          kept     unresolved imports visible in details; a broken tsconfig plus unresolved
                   bare imports makes graph checks Incomplete
        """),
        (new HarnessVersion(3, 7, 0), """
        Release 3.7 additions:
          added    adrs.shape checks decision-record names, numbering, status, date,
                   sections, words, fenced code and tables in the tracked ADR catalogue
          declare  policy.adrs.shape as required and settings.adrs.shape with
                   wordLimit 1000, fencedLineLimit 10 and tableRowLimit 12
        """),
        (new HarnessVersion(3, 8, 0), """
        Release 3.8 additions:
          changed  tracked source directories are measured regardless of their names;
                   generated evidence needs both a toolchain marker and a reviewed
                   generated paths/reason declaration in each scope's .harness.json
          changed  repository-wide diagnostic and linter switches require matching
                   settings.<check>.repositoryWide id/reason entries; stale entries fail
          changed  inline nolint/noqa always blocks, including directives with a reason
          review   the generated and repositoryWide fragments printed below, then run check
        """),
    ];

    private static (string? Text, string? Failure) SplitArchitecturePolicy(string text)
    {
        var document = JsonNode.Parse(text, documentOptions: ConfigJson.ParseOptions)!;
        if (document["policy"] is not JsonObject policy
            || !policy.TryGetPropertyValue(SlicedDotNetShapeCheck.Family, out var old))
        {
            return (text, null);
        }
        if (old is not JsonValue value || !value.TryGetValue<string>(out var mode)
            || mode is not ("required" or "advisory" or "off"))
        {
            return (null, "Cannot migrate architecture.sliced-dotnet policy: expected required, advisory or off.");
        }
        foreach (var rule in SlicedDotNetShapeCheck.Rules.Keys)
        {
            var id = $"{SlicedDotNetShapeCheck.Family}.{rule}";
            if (policy.ContainsKey(id))
            {
                return (null, $"Cannot migrate architecture policy: both aggregate and individual '{id}' are present.");
            }
            policy[id] = mode;
        }
        policy.Remove(SlicedDotNetShapeCheck.Family);
        return (document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", null);
    }

    // Called after ReadPin, so the text is known to parse.
    private static string? DuplicateProperty(string text)
    {
        using var document = JsonDocument.Parse(text, ConfigJson.ParseOptions);
        return ConfigJson.DuplicateProperty(document.RootElement);
    }

    private static (string? Pin, string? Failure) ReadPin(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, ConfigJson.ParseOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.String)
            {
                return (null, "'version' must be a string before the frame can be upgraded.");
            }

            return (version.GetString(), null);
        }
        catch (JsonException exception)
        {
            return (null, $"'{HarnessConfig.FileName}' is not readable as JSON ({exception.Message}).");
        }
    }

    private static bool IsNewer(HarnessVersion candidate, HarnessVersion current)
        => candidate.Major > current.Major
            || (candidate.Major == current.Major && candidate.Minor > current.Minor)
            || (candidate.Major == current.Major
                && candidate.Minor == current.Minor
                && candidate.Patch > current.Patch);

    private static string? Rewrite(string path, string text, string from, string target)
    {
        var current = $"\"{from}\"";
        var key = text.IndexOf("\"version\"", StringComparison.Ordinal);
        var value = key < 0 ? -1 : text.IndexOf(current, key, StringComparison.Ordinal);
        if (value < 0)
        {
            return $"Could not find the pinned value {current} in '{path}'.";
        }

        try
        {
            File.WriteAllText(path, string.Concat(
                text.AsSpan(0, value),
                $"\"{target}\"",
                text.AsSpan(value + current.Length)));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Could not write '{path}': {exception.Message}";
        }
    }
}
