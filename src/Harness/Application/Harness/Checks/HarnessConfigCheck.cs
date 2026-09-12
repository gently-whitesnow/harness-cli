using Harness.Config;
using Harness.Repository;
using Harness.Versioning;

namespace Harness.Checks;

/// <summary>
/// Whether the repository has a harness frame at all, and whether it is internally sound.
/// It runs first because every frame question reads its result, and it is the one place
/// a repository that has never seen this tool is told what to write.
/// </summary>
internal sealed class HarnessConfigCheck : IRepositoryCheck
{
    public string Id => "harness.config";

    public string Group => "harness";

    /// <summary>
    /// The frame itself. `init` writes it and deliberately does not stage it, so the file being
    /// present and untracked is the ordinary first state of a repository meeting this tool.
    /// </summary>
    public IReadOnlyList<EvidenceFile> Evidence => [new(HarnessConfig.FileName)];

    public string Summary => $"tracked {HarnessConfig.FileName} the rest of the run reads";

    public string Explanation =>
        $"""
        Rationale
          The harness holds the same frame over every repository it is pointed at, and the
          frame is a document the repository owns rather than a flag someone passes. The
          root frame can register independent project frames; each tracked file carries the
          answers and check policy that reviewers, agents and CI read for its own scope.

        What it reads
          The tracked root {HarnessConfig.FileName}, plus every project config explicitly
          registered in its `projects` array. All frames are validated before selection;
          an invalid unselected project still makes the workspace incomplete. Unregistered
          tracked nested configs are refused, including when the root has no projects.
          A file outside the index is not evidence. `init` does not stage its output.
          The root owns files outside registered projects; each project owns its directory.
          Checks use project-relative inventory and can read named tracked ancestor
          .editorconfig and Directory.* build evidence without including sibling sources.

        What it accepts
          version       required only in the root; the current contract "{HarnessVersion.Current}",
                        or "latest" to follow the installed binary. Projects cannot declare it.
                        For a different pin, `harness upgrade` updates the root contract.
          projects      optional root-only array of normalized repository-relative directories.
                        Each must contain a tracked {HarnessConfig.FileName}; duplicates,
                        overlapping directories and glob patterns are refused.
          policy        the checks this repository runs, each `required`, `advisory` or `off`.
                        A shipped check the policy does not name is outside the frame: it does
                        not run and the report counts it once under "outside the frame".
          applicability every axis a named check belongs to, marked true or false with reason;
                        an axis no named check belongs to needs no entry.
          settings      an explicit object, with one complete section per configurable check
                        named in policy (comments.*, duplication.*, complexity.*). Only the
                        root declares `settings.commits` and `policy.commits.setup`.
                        Projects share that root version and commit envelope; answers,
                        applicability, policy, architecture and other settings never inherit.
                        A settings section without a policy entry is refused.
          answers       one self-reported answer for every `frame` question the policy names,
                        keyed without the `frame.` prefix; other questions may be answered.
          architecture  required once an architecture.sliced-dotnet check is named: the
                        sliced-dotnet/1 standard, or not applicable with a reason.

        Workspace runs
          `harness check` runs the root and all projects. `--project <directory>` runs the
          root and that registered project and reports partial coverage. Findings name the
          frame, config and policy. Duplication and dependency graphs are measured within
          each frame; cross-project duplication and graph relationships are not measured.

        Why it is incomplete rather than a violation
          Without a readable frame the harness cannot state what this repository answers, so
          it has proved nothing about it. A run that cannot establish anything is incomplete
          (exit 2), which is distinct from having proved a violation (exit 1).

        Remediation
          Run `harness init` to create an unresolved {HarnessConfig.FileName} scaffold at the
          repository root. It does not overwrite an existing file or stage the new one. The
          repository owner or their agent must investigate and answer each question before
          committing it; the harness never assumes an answer on their behalf. Every missing
          or malformed answer names the exact key at fault.

        {HarnessConfig.Template}
        """;

    public CheckEvaluation Evaluate(CheckContext context)
        => context.Config is null
            ? CheckEvaluation.Incomplete(context.ConfigFailure!)
            : CheckEvaluation.Passed(Describe(context.Config));

    private static string Describe(HarnessConfig config)
    {
        var parts = new List<string>
        {
            $"{config.Answers.Count} answer{(config.Answers.Count == 1 ? "" : "s")}",
        };

        if (config.AnswerFailures.Count > 0)
        {
            parts.Add(
                $"{config.AnswerFailures.Count} answer{(config.AnswerFailures.Count == 1 ? "" : "s")} to complete");
        }

        if (config.ArchitectureFailure is not null)
        {
            parts.Add("architecture section to complete");
        }

        if (config.Policy.Count > 0)
        {
            parts.Add($"{config.Policy.Count} explicit policy entr{(config.Policy.Count == 1 ? "y" : "ies")}");
        }

        return $"{HarnessConfig.FileName} contains {string.Join(", ", parts)}. Answers are self-reported; "
            + "the harness validates their form and does not fact-check them.";
    }
}
