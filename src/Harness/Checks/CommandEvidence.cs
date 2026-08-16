using System.Text.RegularExpressions;
using Harness.Processes;

namespace Harness.Checks;

/// <summary>
/// How every gate that delegates to an external tool reads that tool's output. The tools
/// differ; what to do with what they print does not: prefer the locations they already
/// reported, bound how much of it reaches the report, and never let output the harness
/// could not parse become silence.
/// </summary>
internal static class CommandEvidence
{
    /// <summary>Enough located findings to act on; more of the same does not change the next step.</summary>
    public const int FindingLimit = 20;

    /// <summary>
    /// Tools split diagnostics across both streams inconsistently, so evidence is read from
    /// the two of them together.
    /// </summary>
    public static string Output(ProcessResult run)
        => run.StandardOutput + '\n' + run.StandardError;

    public static List<Finding> Collect(
        MatchCollection matches,
        Func<Match, string> location,
        Func<Match, string> message)
        => matches
            .Select(match => new Finding(FindingSeverity.Blocking, location(match), message(match)))
            .DistinctBy(finding => (finding.Location, finding.Message))
            .Take(FindingLimit)
            .ToList();

    /// <summary>
    /// A non-zero exit the harness could not attribute to a location. The diagnostic text is
    /// preserved so the reader is not left with an unexplained failure.
    /// </summary>
    public static Finding Unlocated(string location, ProcessResult run)
        => new(
            FindingSeverity.Blocking,
            location,
            $"`{run.DisplayCommand}` exited with {run.ExitCode}: {Excerpt(run)}");

    /// <summary>
    /// The few lines that say what went wrong. Lines the caller recognizes as its own tool's
    /// framing are dropped first, because they restate the exit code the report already
    /// shows. When nothing looks like a diagnostic, the last lines are used instead: some
    /// output is always better evidence than none.
    /// </summary>
    /// <param name="isFraming">Recognizes a line that wraps the real output rather than being it.</param>
    public static string Excerpt(ProcessResult run, Func<string, bool>? isFraming = null)
    {
        const int limit = 400;

        var lines = Output(run)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => isFraming is null || !isFraming(line))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var diagnostic = lines
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        var text = string.Join(" | ", diagnostic.Count > 0 ? diagnostic : lines.TakeLast(3));
        if (text.Length == 0)
        {
            return "no diagnostic output";
        }

        return text.Length <= limit ? text : text[..limit] + "…";
    }
}
