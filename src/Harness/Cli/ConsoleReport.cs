using System.Globalization;
using System.Text;
using Harness.Checks;
using Harness.Engine;

namespace Harness.Cli;

/// <summary>
/// Renders a run concisely: overall result first, then only the information that changes the
/// reader's next action. Verbose output also includes successful checks and timings.
/// </summary>
internal static class ConsoleReport
{
    public static string Render(RunReport report, bool verbose, bool focused)
    {
        var text = new StringBuilder();

        if (report.ToolError is not null)
        {
            text.Append("INCOMPLETE  verification could not be completed\n");
            text.Append("  ").Append(report.ToolError).Append('\n');
            return text.ToString();
        }

        text.Append(Headline(report)).Append("  ").Append(report.RepositoryPath).Append('\n');

        if (!verbose && !focused && report.ExitCode == ExitCodes.Success)
        {
            AppendSuccessSummary(text, report);
            return text.ToString();
        }

        var visibleGates = report.Gates.Where(gate => verbose || ShouldShow(gate, focused)).ToList();
        foreach (var gate in visibleGates)
        {
            text.Append("\n  ")
                .Append(Label(gate.Outcome).PadRight(15))
                .Append(gate.Id)
                .Append("  ")
                .Append(gate.Summary)
                .Append("  (")
                .Append(FormatDuration(gate.Duration))
                .Append(")\n");

            if (gate.OutcomeReason is not null)
            {
                text.Append("      ").Append(gate.OutcomeReason).Append('\n');
            }

            AppendFindings(text, gate.Findings);
            AppendSuppressed(text, gate.Suppressed);
        }

        if (verbose)
        {
            text.Append("\n  git evidence  (").Append(FormatDuration(report.EvidenceDuration)).Append(")\n");
        }

        if (visibleGates.Count > 0)
        {
            text.Append("\nRun `harness explain <check-id>` for rationale and remediation.\n");
        }

        return text.ToString();
    }

    private static void AppendSuccessSummary(StringBuilder text, RunReport report)
    {
        var advisoryGates = report.Gates
            .Select(gate => (gate.Id, Count: gate.Findings.Count(finding =>
                finding.Severity == FindingSeverity.Advisory)))
            .Where(gate => gate.Count > 0)
            .ToList();
        var readinessGaps = report.Gates
            .Where(gate => gate.Outcome == CheckOutcome.ReadinessGap)
            .Select(gate => gate.Id)
            .ToList();
        var notApplicable = report.Gates
            .Where(gate => gate.Outcome == CheckOutcome.NotApplicable)
            .Select(gate => gate.Id)
            .ToList();
        var excluded = report.Gates
            .Where(gate => gate.Outcome == CheckOutcome.Skipped && gate.OutcomeReason is not null)
            .Select(gate => gate.Id)
            .ToList();
        var suppressed = report.Gates.Sum(gate => gate.Suppressed.Count);

        AppendCountedSummary(text, "advisory findings", advisoryGates);
        AppendNamedSummary(text, "readiness gaps", readinessGaps);
        AppendNamedSummary(text, "not applicable", notApplicable);
        AppendNamedSummary(text, "skipped", excluded);

        if (suppressed > 0)
        {
            text.Append("  ").Append(suppressed).Append(" suppressed findings\n");
        }

        if (advisoryGates.Count > 0 || readinessGaps.Count > 0 || notApplicable.Count > 0
            || excluded.Count > 0 || suppressed > 0)
        {
            text.Append("  Run with --verbose for details.\n");
        }
    }

    private static void AppendCountedSummary(
        StringBuilder text,
        string label,
        IReadOnlyList<(string Id, int Count)> gates)
    {
        if (gates.Count == 0)
        {
            return;
        }

        text.Append("  ")
            .Append(gates.Sum(gate => gate.Count))
            .Append(' ')
            .Append(label)
            .Append(": ")
            .Append(string.Join(", ", gates.Select(gate => $"{gate.Id} ({gate.Count})")))
            .Append('\n');
    }

    private static void AppendNamedSummary(StringBuilder text, string label, IReadOnlyList<string> identifiers)
    {
        if (identifiers.Count > 0)
        {
            text.Append("  ")
                .Append(identifiers.Count)
                .Append(' ')
                .Append(label)
                .Append(": ")
                .Append(string.Join(", ", identifiers))
                .Append('\n');
        }
    }

    private static bool ShouldShow(GateReport gate, bool focused)
        => gate.Outcome switch
        {
            CheckOutcome.Passed => focused || gate.Findings.Count > 0 || gate.Suppressed.Count > 0,
            CheckOutcome.Skipped => gate.OutcomeReason is not null,
            _ => true,
        };

    /// <summary>
    /// Findings the repository has accepted in writing. They are printed with the sentence
    /// that accepted them, because an exception nobody sees is indistinguishable from a
    /// check that was never written.
    /// </summary>
    private static void AppendSuppressed(StringBuilder text, IReadOnlyList<SuppressedFinding> suppressed)
    {
        foreach (var entry in suppressed)
        {
            text.Append("      suppressed  ")
                .Append(entry.Finding.Location)
                .Append(": ")
                .Append(entry.Finding.Message)
                .Append(" — accepted: ")
                .Append(entry.Suppression.Reason)
                .Append('\n');
        }
    }

    /// <summary>
    /// Findings that repeat the same message are reported once with their locations, and
    /// long location lists are truncated with a count, so a repository with many similar
    /// findings still produces output an agent can read.
    /// </summary>
    private static void AppendFindings(StringBuilder text, IReadOnlyList<Finding> findings)
    {
        const int shownLocations = 5;

        var groups = findings
            .GroupBy(finding => (finding.Severity, finding.Message))
            .OrderBy(group => group.Key.Severity);

        foreach (var group in groups)
        {
            var locations = group.Select(finding => finding.Location).ToList();
            var shown = string.Join(", ", locations.Take(shownLocations));
            var remaining = locations.Count - Math.Min(locations.Count, shownLocations);

            text.Append("      ")
                .Append(group.Key.Severity == FindingSeverity.Blocking ? "violation" : "advisory ")
                .Append("  ")
                .Append(shown);

            if (remaining > 0)
            {
                text.Append(" and ").Append(remaining).Append(" more (").Append(locations.Count).Append(" total)");
            }

            text.Append(": ").Append(group.Key.Message).Append('\n');
        }
    }

    /// <summary>
    /// A run in which nothing was actually verified is never reported as PASS: selection
    /// may legitimately narrow a run, but it must not read as a green repository.
    /// </summary>
    private static string Headline(RunReport report)
        => report.ExitCode switch
        {
            ExitCodes.Violation => "FAIL",
            ExitCodes.Success => report switch
            {
                { NothingWasVerified: true } => "NOTHING VERIFIED",
                { HasReadinessGaps: true } => "PASS WITH GAPS",
                _ => "PASS",
            },
            _ => "INCOMPLETE",
        };

    private static string Label(CheckOutcome outcome)
        => outcome switch
        {
            CheckOutcome.Passed => "passed",
            CheckOutcome.Failed => "failed",
            CheckOutcome.Skipped => "skipped",
            CheckOutcome.NotApplicable => "not applicable",
            CheckOutcome.ReadinessGap => "readiness gap",
            _ => "incomplete",
        };

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms";
}
