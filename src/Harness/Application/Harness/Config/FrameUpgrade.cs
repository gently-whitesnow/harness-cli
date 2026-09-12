using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harness.Checks.Architecture;
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
        var document = JsonNode.Parse(text, documentOptions: ParseOptions) as JsonObject;
        var policy = document?["policy"] as JsonObject ?? [];
        var applicability = document?["applicability"] as JsonObject ?? [];
        var detected = FrameAxis.Detected(repository);
        var applicationLanguage = FrameSections.HasApplicationLanguage(
            detected.Select(axis => axis.Key).Concat(applicability.Where(IsApplicable).Select(entry => entry.Key)));

        var report = new StringBuilder();
        foreach (var axis in detected.Where(axis => !applicability.ContainsKey(axis.Key)))
        {
            report.Append($"Sections to add for tracked {axis.Name} sources (or decline the axis with a reason):\n")
                .Append(FrameSections.Indent(FrameSections.AxisFragment(axis, checks, document?["architecture"] is null, applicationLanguage), "  "))
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
                .Append(string.Join(",\n", missing.Select(check => "    " + FrameSections.PolicyEntry(check, applicationLanguage))))
                .Append("\n  }\n");
        }

        return report.ToString();
    }

    private static bool IsApplicable(KeyValuePair<string, JsonNode?> entry)
        => entry.Value is JsonObject axis && axis["applicable"]?.GetValueKind() == JsonValueKind.True;

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

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
                   images.ansible requires a full SHA-256 digest; dependencies.ansible finds
                   role cycles; lint-suppressions.ansible requires reasons for inline noqa
          added    secrets.ansible and role-shape.ansible start advisory: narrow variable-name
                   and role-layout checks calibrated on one configuration repository
          changed  comments.yaml excludes blocks above keys or list items at any depth,
                   with blank lines allowed, trailing comments and tool directives;
                   init defaults it to advisory without C#, Go or TypeScript
          added    harness.coverage reports unknown suffix counts in verbose details;
                   init and frame explanations point to the Ansible toolchain
          kept     explicit policy values and docs.policy; no generated-document exceptions
        """),    ];

    private static (string? Text, string? Failure) SplitArchitecturePolicy(string text)
    {
        var document = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        })!;
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

    private static (string? Pin, string? Failure) ReadPin(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
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
