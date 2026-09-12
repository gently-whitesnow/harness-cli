using System.Globalization;
using System.Text;
using Harness.Versioning;

namespace Harness.Report;

/// <summary>Renders a complete scan as compact status rows, with evidence on demand.</summary>
internal static class ConsoleReport
{
    // "required", "advisory", "off" and "outside" all fit; the header shares the width so the
    // TIME column lines up under --verbose.
    private const int PolicyWidth = 8;

    public static string Render(RunReport report, bool verbose, bool focused, bool all = false, bool showPolicy = false)
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

        // A check outside the frame is not a row: the frame did not ask for it. It is listed
        // once below, so a reviewer still sees what this repository has not decided about.
        var visibleGates = (focused
            ? report.Gates.Where(gate => gate.Outcome != CheckOutcome.Skipped || gate.OutcomeReason is not null)
            : report.Gates.Where(gate => !gate.OutsideFrame)).ToList();
        var identifierWidth = visibleGates.Count == 0
            ? 0
            : Math.Max("CHECK ID".Length, visibleGates.Max(gate => gate.Id.Length)) + 2;

        if (visibleGates.Count > 0)
        {
            text.Append("   ").Append("CHECK ID".PadRight(identifierWidth)).Append("FINDINGS");
            if (showPolicy)
            {
                text.Append("  ").Append("POLICY".PadRight(PolicyWidth));
            }
            if (verbose)
            {
                text.Append("  TIME");
            }

            text.Append('\n');
        }

        foreach (var gate in visibleGates)
        {
            AppendGate(text, gate, verbose, all, identifierWidth, showPolicy);
        }

        AppendOutsideFrame(text, report, verbose, focused);

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

    private static void AppendOutsideFrame(StringBuilder text, RunReport report, bool verbose, bool focused)
    {
        var outside = report.Gates.Where(gate => gate.OutsideFrame).Select(gate => gate.Id).ToList();
        if (outside.Count == 0 || focused)
        {
            return;
        }

        text.Append("\n  outside the frame  ")
            .Append(outside.Count.ToString(CultureInfo.InvariantCulture))
            .Append(outside.Count == 1 ? " check" : " checks")
            .Append(" not named in policy");
        if (verbose)
        {
            text.Append(": ").Append(string.Join(", ", outside));
        }

        text.Append('\n');
    }

    private static void AppendGate(
        StringBuilder text,
        GateReport gate,
        bool verbose,
        bool all,
        int identifierWidth,
        bool showPolicy)
    {
        text.Append(Status(gate)).Append(' ');
        text.Append(gate.Id.PadRight(identifierWidth))
            .Append(IssueCount(gate, all).ToString(CultureInfo.InvariantCulture).PadLeft("FINDINGS".Length));

        if (showPolicy)
        {
            text.Append("  ").Append((gate.Policy ?? "outside").PadRight(PolicyWidth));
        }

        if (verbose)
        {
            text.Append("  ").Append(FormatDuration(gate.Duration));
        }

        text.Append('\n');

        if (!verbose)
        {
            return;
        }

        foreach (var detail in gate.Details)
        {
            text.Append("    ").Append(detail).Append('\n');
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
        => Headline(report.ExitCode, report.NothingWasVerified, report.HasReadinessGaps);

    /// <summary>The one verdict word, shared by a single run and a workspace of runs.</summary>
    public static string Headline(int exitCode, bool nothingWasVerified, bool hasReadinessGaps)
        => exitCode switch
        {
            ExitCodes.Violation => "FAIL",
            ExitCodes.Success => nothingWasVerified ? "NOTHING VERIFIED" : hasReadinessGaps ? "PASS WITH GAPS" : "PASS",
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
