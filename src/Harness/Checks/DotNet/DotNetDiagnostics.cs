using System.Text.RegularExpressions;
using Harness.Processes;

namespace Harness.Checks.DotNet;

/// <summary>
/// Turns SDK output into located findings. The SDK already reports where a problem is;
/// this reads that back rather than restating "the build failed", so a reader can go
/// straight to the file. Output that cannot be located is still reported, bounded, against
/// the target that produced it — an unparsed failure is never downgraded to a pass.
/// </summary>
internal static partial class DotNetDiagnostics
{
    /// <summary>
    /// MSBuild and `dotnet format` diagnostics. Both shapes occur: a diagnostic inside a
    /// file carries a position — "path(line,col): error CODE: message" — while one about
    /// the project as a whole, such as a restore failure, carries none.
    /// </summary>
    [GeneratedRegex(
        @"^(?<path>[^\s(][^(:]*?)(?:\((?<line>\d+)(?:,\d+)?\))?\s*:\s*error\s+"
            + @"(?<code>[A-Za-z0-9_]+):\s*(?<message>[^\r\n]*)$",
        RegexOptions.Multiline)]
    private static partial Regex ErrorDiagnostic { get; }

    /// <summary>A test the runner reported as failed, for example "  Failed Widget.Size [3 ms]".</summary>
    [GeneratedRegex(@"^\s*(?:Failed|failed)\s+(?<name>[^\s\[][^\[\r\n]*?)\s*(?:\[[^\]]*\])?$", RegexOptions.Multiline)]
    private static partial Regex FailedTest { get; }

    /// <summary>
    /// Why a non-zero exit says nothing about the repository. NuGet's NU1xxx codes cover
    /// package resolution and feed access: an unreachable feed, an unauthenticated feed and
    /// a lock file the harness must not update all land here. That evidence is uncertain,
    /// and uncertain evidence must not become a repository violation.
    /// </summary>
    public static string? RestoreFailure(ProcessResult run)
    {
        var restore = ErrorDiagnostic.Matches(CommandEvidence.Output(run))
            .Select(match => match.Groups["code"].Value)
            .Where(code => code.StartsWith("NU", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (restore.Count == 0)
        {
            return null;
        }

        return $"`{run.DisplayCommand}` could not restore packages ({string.Join(", ", restore)}): "
            + $"{CommandEvidence.Excerpt(run)}. This is an environment prerequisite, not repository content.";
    }

    public static IReadOnlyList<Finding> Locate(string rootPath, string target, ProcessResult run)
    {
        var findings = CompilerDiagnostics(rootPath, run);
        return findings.Count > 0 ? findings : [CommandEvidence.Unlocated(target, run)];
    }

    public static IReadOnlyList<Finding> LocateFailedTests(string rootPath, string target, ProcessResult run)
    {
        // A test project that fails to compile reports compiler diagnostics, not failed tests.
        var compilation = CompilerDiagnostics(rootPath, run);
        if (compilation.Count > 0)
        {
            return compilation;
        }

        var failures = CommandEvidence.Collect(
            FailedTest.Matches(CommandEvidence.Output(run)),
            _ => target,
            match => "failing test: " + match.Groups["name"].Value.Trim());

        return failures.Count > 0 ? failures : [CommandEvidence.Unlocated(target, run)];
    }

    private static List<Finding> CompilerDiagnostics(string rootPath, ProcessResult run)
        => CommandEvidence.Collect(
            ErrorDiagnostic.Matches(CommandEvidence.Output(run)),
            match => Location(rootPath, match),
            match => match.Groups["code"].Value + ": " + match.Groups["message"].Value.Trim());

    /// <summary>The file the SDK blamed, with its position when the diagnostic has one.</summary>
    private static string Location(string rootPath, Match match)
    {
        var path = Relative(rootPath, match.Groups["path"].Value.Trim());
        var line = match.Groups["line"];
        return line.Success ? path + ":" + line.Value : path;
    }

    /// <summary>
    /// The SDK reports absolute paths. Reporting them repository-relative keeps findings
    /// comparable with the rest of the run and short enough to read.
    /// </summary>
    private static string Relative(string rootPath, string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return path.Replace('\\', '/');
        }

        var relative = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }
}
