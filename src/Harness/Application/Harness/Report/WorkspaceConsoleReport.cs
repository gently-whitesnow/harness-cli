using System.Text;

namespace Harness.Report;

internal static class WorkspaceConsoleReport
{
    public static string Render(WorkspaceReport report, bool verbose, bool focused, bool all = false)
    {
        if (!report.IsWorkspace && report.Notes.Count == 0 && report.Runs.Count == 1)
        {
            return ConsoleReport.Render(report.Runs[0].Report, verbose, focused, all);
        }

        var text = new StringBuilder();
        text.Append(ConsoleReport.Headline(report.ExitCode, report.NothingWasVerified, report.HasReadinessGaps))
            .Append(report.IsPartial ? "  PARTIAL WORKSPACE RUN" : "  WORKSPACE RUN")
            .Append('\n');
        foreach (var note in report.Notes)
        {
            text.Append("  ").Append(note).Append('\n');
        }

        foreach (var scope in report.Runs)
        {
            text.Append("\nScope: ").Append(scope.Scope).Append(" | config: ").Append(scope.ConfigPath).Append('\n');
            if (report.IsWorkspace)
            {
                text.Append(scope.Scope == "."
                    ? "  Measurement: tracked files outside registered projects.\n"
                    : "  Measurement: this project's tracked files; evidence paths are relative to its directory.\n");
            }

            text.Append(ConsoleReport.Render(scope.Report, verbose, focused, all, showPolicy: report.IsWorkspace));
        }

        if (report.IsWorkspace)
        {
            text.Append("\nBoundary: cross-project duplication and dependency graphs are not measured; ")
                .Append("local metrics do not establish whole-workspace product complexity.\n");
        }

        return text.ToString();
    }
}
