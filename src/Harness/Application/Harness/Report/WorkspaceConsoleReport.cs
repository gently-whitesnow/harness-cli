using System.Text;

namespace Harness.Report;

internal static class WorkspaceConsoleReport
{
    public static string Render(WorkspaceReport report, bool verbose, bool focused, bool all = false)
    {
        if (!report.IsWorkspace && report.Errors.Count == 0 && report.Runs.Count == 1)
        {
            return ConsoleReport.Render(report.Runs[0].Report, verbose, focused, all);
        }

        var text = new StringBuilder();
        text.Append(Headline(report)).Append(report.IsPartial ? "  PARTIAL WORKSPACE RUN" : "  WORKSPACE RUN").Append('\n');
        if (report.IsPartial)
        {
            text.Append("  Root checks and the selected project only; other projects were not measured.\n");
        }

        foreach (var error in report.Errors)
        {
            text.Append("  ").Append(error).Append('\n');
        }

        foreach (var scope in report.Runs)
        {
            text.Append("\nScope: ").Append(scope.Scope).Append(" | config: ").Append(scope.ConfigPath).Append('\n');
            text.Append(scope.Scope == "."
                ? "  Measurement: tracked files outside registered projects.\n"
                : "  Measurement: this project's tracked files; evidence paths are relative to its directory.\n");
            text.Append(ConsoleReport.Render(scope.Report, verbose, focused, all, showPolicy: true));
        }

        if (report.IsWorkspace)
        {
            text.Append("\nBoundary: cross-project duplication and dependency graphs are not measured; ")
                .Append("local metrics do not establish whole-workspace product complexity.\n");
        }

        return text.ToString();
    }

    private static string Headline(WorkspaceReport report)
        => report.ExitCode switch
        {
            ExitCodes.Incomplete => "INCOMPLETE",
            ExitCodes.Violation => "FAIL",
            _ when report.Runs.All(scope => scope.Report.NothingWasVerified) => "NOTHING VERIFIED",
            _ when report.Runs.Any(scope => scope.Report.HasReadinessGaps
                || scope.Report.Gates.Any(gate => gate.Findings.Count > 0)) => "PASS WITH GAPS",
            _ => "PASS",
        };
}
