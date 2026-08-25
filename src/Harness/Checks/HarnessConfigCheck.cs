using Harness.Config;

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
          frame is a document the repository owns rather than a flag someone passes. One
          tracked file therefore carries its answers, check policy and accepted findings — and the same
          file is what a reviewer, an agent and CI all read.

        What it reads
          The tracked {HarnessConfig.FileName} at the repository root, and nothing else. An
          untracked file does not exist for the harness, so every developer, agent and CI
          job reads the same frame. `init` writes the file without staging it, so a run that
          finds it in the working tree only says so under `not in the index`.

        What it accepts
          version       required; the harness release this repository is pinned to, such as
                        "1.0.0", or "latest" to follow the installed binary. The pin selects
                        the questions asked and the checks that run, so a newer binary
                        reproduces it rather than adding to it.
          answers       one self-reported answer for every `frame` question in the selected
                        version, keyed without the `frame.` prefix.
          applicability shared analysis family (currently `csharp`) answered not applicable.
          settings      thresholds and commit language/setup requirements.
          policy        exceptions to the default `required`: `advisory` keeps findings visible
                        without blocking, and `off` skips a check.
          suppress      accepted findings, each naming `check`, `location` and `reason`.

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

        if (config.Policy.Count > 0)
        {
            parts.Add($"{config.Policy.Count} policy override{(config.Policy.Count == 1 ? "" : "s")}");
        }

        if (config.Suppressions.Count > 0)
        {
            parts.Add($"{config.Suppressions.Count} named exception{(config.Suppressions.Count == 1 ? "" : "s")}");
        }

        return $"{HarnessConfig.FileName} contains {string.Join(", ", parts)}. Answers are self-reported; "
            + "the harness validates their form and does not fact-check them.";
    }
}
