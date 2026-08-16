using System.Text.RegularExpressions;
using Harness.Processes;

namespace Harness.Checks.Web;

/// <summary>
/// Turns script output into located findings. The repository's own tools already report
/// where a problem is; this reads that back rather than restating "lint failed", so a
/// reader can go straight to the file. Output that cannot be located is still reported,
/// bounded, against the manifest that declared the script — an unparsed failure is never
/// downgraded to a pass.
/// </summary>
internal static partial class WebDiagnostics
{
    /// <summary>TypeScript's diagnostic shape: "src/main.ts(3,5): error TS2322: message".</summary>
    [GeneratedRegex(
        @"^(?<path>[^\s(][^(\r\n]*?)\((?<line>\d+),\d+\):\s*error\s+(?<code>TS\d+):\s*(?<message>[^\r\n]*)$",
        RegexOptions.Multiline)]
    private static partial Regex TypeScriptDiagnostic { get; }

    /// <summary>
    /// The colon-separated shape most other Node tools emit: "src/main.ts:3:5: error message".
    /// </summary>
    [GeneratedRegex(
        @"^(?<path>[^\s:][^\s:]*\.[A-Za-z]+):(?<line>\d+):\d+:?\s+(?:error|Error)\s*:?\s*(?<message>[^\r\n]*)$",
        RegexOptions.Multiline)]
    private static partial Regex ColonDiagnostic { get; }

    /// <summary>
    /// Lines the package manager adds around the script it ran. They report that something
    /// failed, which the exit code already said, and would crowd out the tool's own evidence.
    /// </summary>
    [GeneratedRegex(@"^(npm (error|ERR!|notice|warn)|yarn |ELIFECYCLE| ELIFECYCLE)", RegexOptions.IgnoreCase)]
    private static partial Regex PackageManagerFraming { get; }

    /// <param name="rootPath">Absolute repository root, so an absolute path can be reported relative to it.</param>
    /// <param name="manifestPath">Manifest whose script ran; its directory anchors relative paths.</param>
    public static IReadOnlyList<Finding> Locate(string rootPath, string manifestPath, ProcessResult run)
    {
        var output = CommandEvidence.Output(run);
        var located = Collect(rootPath, manifestPath, TypeScriptDiagnostic.Matches(output));
        if (located.Count == 0)
        {
            located = Collect(rootPath, manifestPath, ColonDiagnostic.Matches(output));
        }

        return located.Count > 0
            ? located
            : [new Finding(
                FindingSeverity.Blocking,
                manifestPath,
                $"`{run.DisplayCommand}` exited with {run.ExitCode}: "
                    + CommandEvidence.Excerpt(run, PackageManagerFraming.IsMatch))];
    }

    private static List<Finding> Collect(string rootPath, string manifestPath, MatchCollection matches)
        => CommandEvidence.Collect(
            matches,
            match => Location(rootPath, manifestPath, match.Groups["path"].Value.Trim())
                + ":" + match.Groups["line"].Value,
            Message);

    private static string Message(Match match)
    {
        var code = match.Groups["code"];
        var message = match.Groups["message"].Value.Trim();
        return code.Success ? code.Value + ": " + message : message;
    }

    /// <summary>
    /// A path as the rest of the run reports paths: relative to the repository root. Tools
    /// report relative to the directory they ran in, which is the manifest's directory, so a
    /// finding in a workspace member stays navigable from the root.
    /// </summary>
    private static string Location(string rootPath, string manifestPath, string path)
    {
        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized))
        {
            var relative = Path.GetRelativePath(rootPath, normalized).Replace('\\', '/');
            return relative.StartsWith("..", StringComparison.Ordinal) ? normalized : relative;
        }

        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        var separator = manifestPath.LastIndexOf('/');
        return separator < 0 ? normalized : manifestPath[..separator] + "/" + normalized;
    }
}
