using Harness.Config;
using Harness.Repository;

namespace Harness.Checks;

/// <summary>
/// Whether every stack the index shows has been decided about. A language with tracked
/// sources but no applicability entry is a feature the repository forgot rather than declined:
/// under the explicit-only frame its checks silently stay outside. The check names the axis
/// and prints the fragment `init` would have written, and leaves the paste to the owner —
/// the harness never edits a tracked file. The composition root hands it what the binary ships.
/// </summary>
internal sealed class HarnessCoverageCheck(Func<IReadOnlyList<CheckDescriptor>> shipped) : IRepositoryCheck
{
    private const int ShownSources = 3;

    public string Id => "harness.coverage";

    public string Group => "harness";

    /// <summary>Every source shape an axis is detected by; a run names those left unstaged.</summary>
    public IReadOnlyList<EvidenceFile> Evidence => FrameAxis.All.SelectMany(axis => axis.Sources).Distinct().ToList();

    public string Summary => "every detected language or stack is declared in the frame";

    public string Explanation =>
        $$"""
        Rationale
          The frame names what a repository runs, and a check the policy does not name is
          outside it. That is the right model for a repository that has decided; it is a
          silent hole for one that added a second language and never came back to the frame.
          This check turns the hole into a finding: a stack the index shows must be declared
          applicable or declined with a reason.

        What it reads
          The Git index, through the source shapes of every axis the harness ships:
          {{string.Join(", ", FrameAxis.All.Select(axis => $"{axis.Key} ({string.Join(" ", axis.Sources.Select(source => source.Name))})"))}}.
          Declared generated sources and toolchain-ignored paths are not counted. Root YAML file
          contents are read to detect Ansible plays with `hosts:`. Verbose details count
          suffixes outside the axes, excluding Markdown and dot-files; this is not a finding.

        Rule
          For each axis with at least one tracked source, `applicability.<axis>` must exist —
          `{ "applicable": true }` or `{ "applicable": false, "reason": "..." }`. An axis
          without tracked sources needs no entry and cannot claim applicability. A declined axis passes: the decision is
          tracked and reviewed, which is what the check asks for.
          The `generated` declarations answer to the same index: a tracked file with a
          toolchain-generated marker outside every declared path blocks, and so does a
          declared path with no tracked files under it, as a stale declaration.

        Remediation
          The finding prints the fragment `harness init` would have written for the axis: its
          applicability entry, the settings sections of its checks with the contract defaults,
          and their policy entries. Paste each object into the matching section of
          {{HarnessConfig.FileName}}, adjust the numbers knowingly, and commit. To decline the
          axis instead, declare it not applicable with a reason. For an undeclared marker,
          add its path and reason to `generated` or remove the marker; for a stale path,
          remove the declaration. The harness does not edit the tracked file.

        Decisions
          adrs/0054-explicit-only-frame.md
          adrs/0067-reviewed-weakening-in-the-frame.md
        """;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        if (context.Config is null)
        {
            return CheckEvaluation.Incomplete(context.ConfigFailure!);
        }

        var checks = shipped();
        var findings = new List<Finding>();
        var details = new List<string>();
        foreach (var entry in context.Repository.TrackedEntries)
        {
            var classification = context.Repository.Classify(entry);
            if (classification == EvidenceKind.UndeclaredMarker)
            {
                findings.Add(new Finding(FindingSeverity.Blocking, entry.Path,
                    $"generated marker marks source outside 'generated'; declare its path and reason in .harness.json, or remove the marker"));
            }
        }

        foreach (var declaration in context.Config.Generated)
        {
            foreach (var path in declaration.Paths.Where(path => !context.Repository.TrackedEntries.Any(entry =>
                entry.Path == path || entry.Path.StartsWith(path + "/", StringComparison.Ordinal))))
            {
                findings.Add(new Finding(FindingSeverity.Blocking, HarnessConfig.FileName,
                    $"generated path '{path}' has no tracked files; remove this stale declaration"));
            }
        }
        var detected = FrameAxis.All
            .Select(axis => (Axis: axis, Sources: axis.Detect(context.Repository, axis.Sources.SelectMany(context.Tracked))))
            .ToList();
        foreach (var (axis, sources) in detected)
        {
            if (sources.Count == 0)
            {
                if (context.Config.Applicability.TryGetValue(axis.Key, out var asserted) && asserted.IsApplicable)
                {
                    findings.Add(new Finding(FindingSeverity.Blocking, HarnessConfig.FileName,
                        $"applicability.{axis.Key} is true, but no tracked authored sources establish that stack"));
                }
                continue;
            }
            if (context.Config.Applicability.TryGetValue(axis.Key, out var answer))
            {
                details.Add($"{axis.Key}: {sources.Count} tracked source{(sources.Count == 1 ? "" : "s")}, "
                    + (answer.IsApplicable ? "applicable" : $"declined — \"{answer.Reason}\""));
                continue;
            }

            var shown = string.Join(", ", sources.Take(ShownSources));
            var remaining = sources.Count - Math.Min(sources.Count, ShownSources);
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                sources[0],
                $"{sources.Count} tracked {axis.Name} source{(sources.Count == 1 ? "" : "s")} "
                    + $"({shown}{(remaining > 0 ? $" and {remaining} more" : "")}) but no `applicability.{axis.Key}` entry; "
                    + $"add these sections to {HarnessConfig.FileName}, or declare "
                    + $"\"{axis.Key}\": {{ \"applicable\": false, \"reason\": \"...\" }}:\n"
                    + FrameSections.Indent(FrameSections.AxisFragment(axis, checks, context.Config.Architecture is null), "      ")));
        }

        if (SourcesOfNoAxis(context) is { } outside)
        {
            details.Add(outside);
        }

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "every detected axis is declared in the frame." : null,
            details: details);
    }

    /// <summary>The suffixes no axis reads, counted: the hole ADR-0054 named, visible without a finding.</summary>
    private static string? SourcesOfNoAxis(CheckContext context)
    {
        var counts = context.Repository.TrackedEntries
            .Where(entry => !entry.IsSymbolicLink)
            .Where(entry => !FrameAxis.All.Any(axis => axis.Sources.Any(source => source.Matches(entry.Path))))
            .Select(entry => entry.Path[(entry.Path.LastIndexOf('/') + 1)..])
            .Where(name => !name.StartsWith('.') && !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(name => name.LastIndexOf('.') is var dot && dot > 0 ? name[dot..].ToLowerInvariant() : NoSuffix)
            .GroupBy(suffix => suffix, StringComparer.Ordinal)
            .Select(group => (Suffix: group.Key, Count: group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Suffix == NoSuffix ? 1 : 0)
            .ThenBy(entry => entry.Suffix, StringComparer.Ordinal)
            .ToList();
        return counts.Count == 0
            ? null
            : "sources of no axis: " + string.Join(", ", counts.Select(entry => $"{entry.Suffix} {entry.Count}"));
    }

    private const string NoSuffix = "no suffix";
}
