using Harness.Repository;

namespace Harness.Checks.Verify;

/// <summary>Verifies the repository's unified verification script from the Git index.</summary>
internal sealed class VerifyEntrypointCheck : IRepositoryCheck
{
    public string Id => "verify.entrypoint";

    public string Group => "verify";

    public IReadOnlyList<EvidenceFile> Evidence => [];

    public string Summary => "tracked executable verification entry point";

    public string Explanation => """
        Rationale
          The harness never runs the repository's own build, tests or linters. One tracked,
          executable entry point composes the repository toolchain together with `harness check`
          so agents, reviewers and CI run the same command.

        Rule
          settings."verify.entrypoint".path names a repository-relative path (default "verify.sh").
          The check proves from the Git index, without executing anything, that:
          - the path is tracked in the Git index;
          - its Git mode is 100755 (regular executable file, not 100644 and not a symbolic link);
          - the file is non-empty;
          - at least one non-comment line invokes harness check (e.g. `harness check`,
            `"$HARNESS" check` or `dotnet run ... Harness.Host.csproj -- check`).

        Remediation
          Create or stage the script, mark it executable (`chmod +x verify.sh && git add verify.sh`),
          and invoke `harness check` alongside the repository's own build, test and lint commands.
        """;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var settings = context.Config?.Settings.VerifyEntrypoint;
        if (settings is null)
        {
            return CheckEvaluation.Incomplete("settings.verify.entrypoint is missing");
        }

        var path = settings.Path;
        var entry = context.Repository.TrackedEntries
            .FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.Ordinal));
        if (entry is null)
        {
            var onDisk = File.Exists(Path.Combine(context.Repository.RootPath, path));
            var hint = onDisk
                ? $"is present in the working tree but not in the index; run `chmod +x {path} && git add {path}`"
                : $"is not tracked by Git; create an executable verification script at '{path}' and stage it";
            return CheckEvaluation.From([new Finding(FindingSeverity.Blocking, path, hint)]);
        }

        var findings = new List<Finding>();
        if (entry.IsSymbolicLink)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                path,
                "is a symbolic link; verification entry point must be a tracked regular executable file (mode 100755)"));
            return CheckEvaluation.From(findings);
        }

        if (!entry.IsExecutable)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                path,
                $"Git index mode is {entry.Mode}, expected executable 100755; run `chmod +x {path} && git add {path}`"));
        }

        var (text, failure) = context.Repository.ReadTrackedText(entry);
        if (text is null)
        {
            return CheckEvaluation.Incomplete(failure ?? $"Could not read '{path}'.", findings);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                path,
                "is empty; add the repository verification commands including `harness check`"));
            return CheckEvaluation.From(findings);
        }

        if (!InvokesHarnessCheck(text))
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                path,
                "does not invoke `harness check` on any non-comment line; run harness check from this script"));
        }

        return CheckEvaluation.From(findings);
    }

    private static bool InvokesHarnessCheck(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Contains("harness", StringComparison.OrdinalIgnoreCase)
                && line.Contains("check", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string StripComment(string line)
    {
        var singleQuoted = false;
        var doubleQuoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '\\' && !singleQuoted && index + 1 < line.Length)
            {
                index++;
                continue;
            }

            if (character == '\'' && !doubleQuoted)
            {
                singleQuoted = !singleQuoted;
            }
            else if (character == '"' && !singleQuoted)
            {
                doubleQuoted = !doubleQuoted;
            }
            else if (character == '#' && !singleQuoted && !doubleQuoted
                && (index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                return line[..index];
            }
        }

        return line;
    }
}
