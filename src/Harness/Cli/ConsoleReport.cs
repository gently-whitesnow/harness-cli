using System.Globalization;
using System.Text;
using Harness.Checks;
using Harness.Engine;

namespace Harness.Cli;

/// <summary>
/// Renders a run concisely: overall result first, then the information that changes the
/// reader's next action — blocking violations, advisory findings, incomplete and skipped
/// checks — and the cost of every gate.
/// </summary>
internal static class ConsoleReport
{
    public static string Render(RunReport report)
    {
        var text = new StringBuilder();

        if (report.ToolError is not null)
        {
            text.Append("INCOMPLETE  verification could not be completed\n");
            text.Append("  ").Append(report.ToolError).Append('\n');
            return text.ToString();
        }

        text.Append(Headline(report)).Append("  ").Append(report.RepositoryPath).Append('\n');

        foreach (var gate in report.Gates)
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

            AppendCommands(text, gate.Commands);
            AppendFindings(text, gate.Findings);
        }

        text.Append("\n  git evidence  (").Append(FormatDuration(report.EvidenceDuration)).Append(")\n");
        text.Append("\nRun `harness explain <check-id>` for rationale and remediation.\n");
        return text.ToString();
    }

    /// <summary>
    /// Every command a gate ran, so a reader can reproduce the failure outside the harness
    /// and see what the gate actually cost.
    /// </summary>
    private static void AppendCommands(StringBuilder text, IReadOnlyList<ExecutedCommand> commands)
    {
        foreach (var command in commands)
        {
            text.Append("      ran        ")
                .Append(command.DisplayCommand)
                .Append("  exit ")
                .Append(command.ExitCode)
                .Append("  (")
                .Append(FormatDuration(command.Duration))
                .Append(")\n");
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
            ExitCodes.Success => report.NothingWasVerified ? "NOTHING VERIFIED" : "PASS",
            _ => "INCOMPLETE",
        };

    private static string Label(CheckOutcome outcome)
        => outcome switch
        {
            CheckOutcome.Passed => "passed",
            CheckOutcome.Failed => "failed",
            CheckOutcome.Skipped => "skipped",
            CheckOutcome.NotApplicable => "not applicable",
            _ => "incomplete",
        };

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms";
}
