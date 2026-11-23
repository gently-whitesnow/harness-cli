using System.Text;
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
          - at least one shell command invokes `harness check` without narrowing (`--only`/`--skip`)
            and without silencing its exit status (`|| true`).

        Remediation
          Create or stage the script, mark it executable (`git add --chmod=+x verify.sh`),
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
            var (untracked, _) = context.Repository.ReadUntrackedPaths();
            var onDisk = untracked is not null && untracked.Contains(path, StringComparer.Ordinal);
            var hint = onDisk
                ? $"is present in the working tree but not in the index; run `git add --chmod=+x {path}`"
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
                $"Git index mode is {entry.Mode}, expected executable 100755; run `git add --chmod=+x {path}`"));
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
                "does not invoke `harness check` (without `--only`/`--skip` or `|| true`) on any executable command"));
        }

        return CheckEvaluation.From(findings);
    }

    private static bool InvokesHarnessCheck(string text)
    {
        var logicalLines = JoinContinuations(text);
        foreach (var logical in logicalLines)
        {
            var commands = SplitCommands(logical);
            for (var index = 0; index < commands.Count; index++)
            {
                var (command, separatorAfter) = commands[index];
                if (!IsHarnessCheckCommand(command))
                {
                    continue;
                }

                if (separatorAfter == "||"
                    && index + 1 < commands.Count
                    && IsSilencingFallback(commands[index + 1].Command))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool IsHarnessCheckCommand(string command)
    {
        var tokens = Tokenize(command);
        if (tokens.Count < 2)
        {
            return false;
        }

        // Skip leading env assignments (FOO=bar) and helper wrappers like `run "Harness"` or `exec`.
        var start = 0;
        while (start < tokens.Count && IsEnvAssignment(tokens[start]))
        {
            start++;
        }

        if (start >= tokens.Count)
        {
            return false;
        }

        var head = Unquote(tokens[start]);
        if (head is "echo" or "printf" or "true" or ":")
        {
            return false;
        }

        if (tokens.Any(token => Unquote(token) is "--only" or "--skip"))
        {
            return false;
        }

        // Direct or wrapper invocation: `<harness-binary-or-var> check ...`
        for (var index = start; index + 1 < tokens.Count; index++)
        {
            var current = Unquote(tokens[index]);
            var next = Unquote(tokens[index + 1]);
            if (IsHarnessExecutableToken(current) && string.Equals(next, "check", StringComparison.Ordinal))
            {
                return true;
            }

            // `dotnet run --project ...Harness.Host.csproj -- check`
            if (current.EndsWith("Harness.Host.csproj", StringComparison.OrdinalIgnoreCase)
                && tokens.Skip(index + 1).Any(token => string.Equals(Unquote(token), "check", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHarnessExecutableToken(string token)
    {
        if (token.Length == 0 || token.StartsWith('-'))
        {
            return false;
        }

        if (string.Equals(token, "harness", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (token.EndsWith("/harness", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Shell variable reference such as $HARNESS, ${HARNESS}, $harness_bin
        return token.StartsWith('$') && token.Contains("harness", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSilencingFallback(string nextCommand)
    {
        var tokens = Tokenize(nextCommand);
        return tokens.Count > 0 && Unquote(tokens[0]) is "true" or ":";
    }

    private static bool IsEnvAssignment(string token)
    {
        var eq = token.IndexOf('=');
        return eq > 0 && !token.StartsWith('-') && !token.StartsWith('"') && !token.StartsWith('\'');
    }

    private static string Unquote(string token)
        => token.Length >= 2 && ((token[0] == '"' && token[^1] == '"') || (token[0] == '\'' && token[^1] == '\''))
            ? token[1..^1]
            : token;

    private static List<string> JoinContinuations(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var stripped = StripComment(raw.TrimEnd('\r')).TrimEnd();
            if (stripped.EndsWith('\\') && !stripped.EndsWith("\\\\", StringComparison.Ordinal))
            {
                current.Append(stripped[..^1]).Append(' ');
                continue;
            }

            current.Append(stripped);
            var combined = current.ToString().Trim();
            if (combined.Length > 0)
            {
                result.Add(combined);
            }

            current.Clear();
        }

        if (current.Length > 0)
        {
            var trailing = current.ToString().Trim();
            if (trailing.Length > 0)
            {
                result.Add(trailing);
            }
        }

        return result;
    }

    private static List<(string Command, string SeparatorAfter)> SplitCommands(string line)
    {
        var result = new List<(string, string)>();
        var current = new StringBuilder();
        var singleQuoted = false;
        var doubleQuoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '\\' && !singleQuoted && index + 1 < line.Length)
            {
                current.Append(character).Append(line[++index]);
                continue;
            }

            if (character == '\'' && !doubleQuoted)
            {
                singleQuoted = !singleQuoted;
                current.Append(character);
                continue;
            }

            if (character == '"' && !singleQuoted)
            {
                doubleQuoted = !doubleQuoted;
                current.Append(character);
                continue;
            }

            if (!singleQuoted && !doubleQuoted)
            {
                if (character == ';'
                    || (character == '&' && index + 1 < line.Length && line[index + 1] == '&')
                    || (character == '|' && index + 1 < line.Length && line[index + 1] == '|')
                    || character == '|')
                {
                    var sep = character == ';' ? ";"
                        : character == '&' ? "&&"
                        : index + 1 < line.Length && line[index + 1] == '|' ? "||"
                        : "|";
                    if (sep.Length == 2)
                    {
                        index++;
                    }

                    result.Add((current.ToString().Trim(), sep));
                    current.Clear();
                    continue;
                }
            }

            current.Append(character);
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0)
        {
            result.Add((tail, string.Empty));
        }

        return result;
    }

    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var singleQuoted = false;
        var doubleQuoted = false;
        for (var index = 0; index < command.Length; index++)
        {
            var character = command[index];
            if (character == '\\' && !singleQuoted && index + 1 < command.Length)
            {
                current.Append(command[++index]);
                continue;
            }

            if (character == '\'' && !doubleQuoted)
            {
                singleQuoted = !singleQuoted;
                current.Append(character);
                continue;
            }

            if (character == '"' && !singleQuoted)
            {
                doubleQuoted = !doubleQuoted;
                current.Append(character);
                continue;
            }

            if (!singleQuoted && !doubleQuoted && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
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
