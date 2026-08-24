using System.Globalization;
using System.Text;
using Harness.Checks;
using Harness.Engine;

namespace Harness.Cli;

/// <summary>Renders a complete scan as compact status rows, with evidence on demand.</summary>
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
            AppendGate(text, gate, verbose, identifierWidth);
        }

        if (verbose)
        {
            text.Append("\n  git evidence  (").Append(FormatDuration(report.EvidenceDuration)).Append(")\n");
        }

        if (report.Gates.Any(gate => gate.Outcome is CheckOutcome.Failed or CheckOutcome.Incomplete))
        {
            text.Append("\nDetails: harness check --only <check-id> --verbose\n");
            text.Append("harness check [path] [--only <ids>] [--skip <ids>] [--verbose]\n");
        }

        return text.ToString();
    }

    private static void AppendGate(StringBuilder text, GateReport gate, bool verbose, int identifierWidth)
    {
        text.Append(Status(gate)).Append(' ');
        text.Append(gate.Id.PadRight(identifierWidth))
            .Append(IssueCount(gate).ToString(CultureInfo.InvariantCulture).PadLeft("FINDINGS".Length));

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

        AppendFindings(text, gate.Findings);
        AppendSuppressed(text, gate.Suppressed);
    }

    private static string Status(GateReport gate)
        => gate.Outcome switch
        {
            CheckOutcome.Passed when gate.Findings.Count == 0 && gate.Suppressed.Count == 0 => "✅",
            CheckOutcome.Passed => "⚠️",
            CheckOutcome.Failed => "❌",
            CheckOutcome.Incomplete => "❌",
            CheckOutcome.ReadinessGap => "⚠️",
            CheckOutcome.NotApplicable => "➖",
            _ => "⏭️",
        };

    private static int IssueCount(GateReport gate)
    {
        var reported = gate.Findings.Count + gate.Suppressed.Count;
        if (reported > 0)
        {
            return reported;
        }

        return gate.Outcome is CheckOutcome.Failed or CheckOutcome.Incomplete or CheckOutcome.ReadinessGap
            ? 1
            : 0;
    }

    private static void AppendSuppressed(StringBuilder text, IReadOnlyList<SuppressedFinding> suppressed)
    {
        foreach (var entry in suppressed)
        {
            text.Append("    suppressed  ")
                .Append(entry.Finding.Location)
                .Append(": ")
                .Append(entry.Finding.Message)
                .Append(" — accepted: ")
                .Append(entry.Suppression.Reason)
                .Append('\n');
        }
    }

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

            text.Append("    ")
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
