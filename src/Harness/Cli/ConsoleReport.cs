using System.Globalization;
using System.Text;
using Harness.Checks;
using Harness.Engine;
using Harness.Versioning;

namespace Harness.Cli;

/// <summary>Renders a complete scan as compact status rows, with evidence on demand.</summary>
internal static class ConsoleReport
{
    public static string Render(RunReport report, bool verbose, bool focused, bool all = false)
    {
        var text = new StringBuilder();

        if (report.ToolError is not null)
        {
            text.Append("INCOMPLETE  verification could not be completed\n");
            text.Append("  ").Append(report.ToolError).Append('\n');
            return text.ToString();
        }

        text.Append(Headline(report)).Append("  ").Append(report.RepositoryPath).Append('\n');
        text.Append("  harness ").Append(HarnessVersion.Current);
        if (report.Pin is not null)
        {
            text.Append(" · repository pins ").Append(report.Pin);
        }

        text.Append('\n');

        var visibleGates = (focused
            ? report.Gates.Where(gate => gate.Outcome != CheckOutcome.Skipped || gate.OutcomeReason is not null)
            : report.Gates).ToList();
        var identifierWidth = visibleGates.Count == 0
            ? 0
            : Math.Max("CHECK ID".Length, visibleGates.Max(gate => gate.Id.Length)) + 2;

        if (visibleGates.Count > 0)
        {
            text.Append("   ").Append("CHECK ID".PadRight(identifierWidth)).Append("FINDINGS");
            if (verbose)
            {
                text.Append("  TIME");
            }

            text.Append('\n');
        }

        foreach (var gate in visibleGates)
        {
            AppendGate(text, gate, verbose, all, identifierWidth);
        }

        var untracked = report.UntrackedEvidence ?? [];
        if (verbose)
        {
            text.Append("\n  git evidence  (").Append(FormatDuration(report.EvidenceDuration)).Append(")\n");
        }
        else if (untracked.Count > 0)
        {
            text.Append("\n  git evidence\n");
        }

        AppendUntracked(text, untracked);

        if (report.Gates.Any(gate => gate.Outcome is CheckOutcome.Failed or CheckOutcome.Incomplete))
        {
            text.Append("\nDetails: harness check --only <check-id> --verbose\n");
            text.Append("harness check [path] [--only <ids>] [--skip <ids>] [--verbose] [--all]\n");
        }

        return text.ToString();
    }

    private static void AppendGate(
        StringBuilder text,
        GateReport gate,
        bool verbose,
        bool all,
        int identifierWidth)
    {
        text.Append(Status(gate)).Append(' ');
        text.Append(gate.Id.PadRight(identifierWidth))
            .Append(IssueCount(gate, all).ToString(CultureInfo.InvariantCulture).PadLeft("FINDINGS".Length));

        if (verbose)
        {
            text.Append("  ").Append(FormatDuration(gate.Duration));
        }

        text.Append('\n');

        if (!verbose)
        {
            return;
        }

        text.Append("    outcome: ").Append(Label(gate.Outcome)).Append('\n');

        if (gate.OutcomeReason is not null)
        {
            text.Append("    ").Append(gate.OutcomeReason).Append('\n');
        }

        AppendFindings(text, all ? gate.DetailedFindings : gate.Findings, all);
    }

    private static string Status(GateReport gate)
        => gate.Outcome switch
        {
            CheckOutcome.Passed when gate.Findings.Count == 0 => "✅",
            CheckOutcome.Passed => "⚠️",
            CheckOutcome.Failed => "❌",
            CheckOutcome.Incomplete => "❌",
            CheckOutcome.ReadinessGap => "⚠️",
            CheckOutcome.NotApplicable => "➖",
            _ => "⏭️",
        };

    private static int IssueCount(GateReport gate, bool all)
    {
        var reported = all ? Math.Max(gate.DetailedFindings.Count, gate.Findings.Count) : gate.Findings.Count;
        if (reported > 0)
        {
            return reported;
        }

        return gate.Outcome is CheckOutcome.Failed or CheckOutcome.Incomplete or CheckOutcome.ReadinessGap
            ? 1
            : 0;
    }

    /// <summary>
    /// Names the files the run reads as evidence that exist on disk without being tracked. The
    /// verdict itself stands: Git is what the harness, a reviewer and CI all read. What changes
    /// is that "you have not written it" stops being the only reading of the report.
    /// </summary>
    private static void AppendUntracked(StringBuilder text, IReadOnlyList<string> untracked)
    {
        const int shownPaths = 5;

        if (untracked.Count == 0)
        {
            return;
        }

        text.Append("    not in the index  ").Append(string.Join(", ", untracked.Take(shownPaths)));

        var remaining = untracked.Count - Math.Min(untracked.Count, shownPaths);
        if (remaining > 0)
        {
            text.Append(" and ").Append(remaining).Append(" more (").Append(untracked.Count).Append(" total)");
        }

        text.Append('\n');
        text.Append("    the run reads files with these names as evidence and Git does not see them; "
            + "run\n    `git add` on them, because an untracked file is evidence for nobody.\n");
    }

    private static void AppendFindings(
        StringBuilder text,
        IReadOnlyList<Finding> findings,
        bool all)
    {
        var shownLocations = all ? int.MaxValue : 5;

        var groups = findings
            .GroupBy(finding => (finding.Severity, finding.Message))
            .OrderBy(group => group.Key.Severity);

        foreach (var group in groups)
        {
            var locations = group.Select(finding => finding.Location).ToList();
            var shown = string.Join(", ", locations.Take(shownLocations));
            var remaining = locations.Count - Math.Min(locations.Count, shownLocations);

            text.Append("    ")
                .Append(Label(group.Key.Severity))
                .Append("  ")
                .Append(shown);

            if (remaining > 0)
            {
                text.Append(" and ").Append(remaining).Append(" more (").Append(locations.Count).Append(" total)");
            }

            text.Append(": ").Append(group.Key.Message).Append('\n');
        }
    }

    private static string Label(FindingSeverity severity)
        => severity == FindingSeverity.Blocking ? "violation" : "advisory ";

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
