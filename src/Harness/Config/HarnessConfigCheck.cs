using Harness.Checks;

namespace Harness.Config;

/// <summary>
/// Whether the repository has a harness frame at all, and whether it is internally sound.
/// It runs first because every declaration check reads its result, and it is the one place
/// a repository that has never seen this tool is told what to write.
/// </summary>
internal sealed class HarnessConfigCheck : IRepositoryCheck
{
    public string Id => "harness.config";

    public string Group => "harness";

    public string Summary => $"tracked {HarnessConfig.FileName} the rest of the run reads";

    public string Explanation =>
        $"""
        Rationale
          The harness holds the same frame over every repository it is pointed at, and the
          frame is a document the repository owns rather than a flag someone passes. One
          tracked file therefore decides what this repository claims, how strictly each
          claim is judged, and which findings it has consciously accepted — and the same
          file is what a reviewer, an agent and CI all read.

        What it reads
          The tracked {HarnessConfig.FileName} at the repository root, and nothing else. An
          untracked file does not exist for the harness: what verifies a repository has to
          travel with it, so every developer, agent and CI job reads the same frame.

        What it accepts
          version       optional; must be 1.
          declarations  the repository's answers to the questions the `declaration` checks
                        ask, keyed without the `declaration.` prefix.
          policy        check or group identifier to `required`, `advisory` or `off`.
          suppress      accepted findings, each naming `check`, `location` and `reason`.

        Why it is incomplete rather than a violation
          Without a readable frame the harness cannot state what this repository claims, so
          it has proved nothing about it. A run that cannot establish anything is incomplete
          (exit 2), which is distinct from having proved a violation (exit 1).

        Remediation
          Commit a {HarnessConfig.FileName} at the repository root and track it. The harness
          never writes the file and never assumes an answer on the repository's behalf: an
          unanswered question stays visibly unanswered. Every parse failure names the exact
          key at fault.

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
            $"{config.Declarations.Count} declaration{(config.Declarations.Count == 1 ? "" : "s")}",
        };

        if (config.Policy.Count > 0)
        {
            parts.Add($"{config.Policy.Count} policy override{(config.Policy.Count == 1 ? "" : "s")}");
        }

        if (config.Suppressions.Count > 0)
        {
            parts.Add($"{config.Suppressions.Count} named exception{(config.Suppressions.Count == 1 ? "" : "s")}");
        }

        return $"{HarnessConfig.FileName} declares {string.Join(", ", parts)}. That the frame is well formed is "
            + "not evidence that its answers are true; each declaration check verifies its own.";
    }
}
