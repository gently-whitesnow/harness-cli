using System.Text;
using Harness.Checks;

namespace Harness.Cli;

internal static class UsageText
{
    private const string Usage =
        """
        harness — repository quality CLI

        Usage
          harness check [path] [--only <ids>] [--skip <ids>] [--verbose]
          harness init [path] [--latest] [--language <en|ru>]
          harness upgrade [path] [--to <release>] [--dry-run]
          harness setup [path]
          harness commit-message check <message-file>
          harness commit-message template
          harness commits check <base>..<head>
          harness explain <check-id>
          harness version
          harness help

        Options
          --only <ids>   run one check, or the given check/group identifiers (comma separated)
          --skip <ids>   exclude the given check or group identifiers; they stay visible in the summary
          --verbose      show findings, reasons and timings
          --latest       make an initialized frame follow the newest question set
          --to <release> raise the pin to this release instead of the installed one
          --dry-run      report what raising the pin would take on, and write nothing
          --language     language for human-written commit subjects and bodies

        Exit codes
          0  every selected applicable blocking check completed and passed
          1  a selected applicable blocking check proved a violation
          2  verification could not be completed reliably

        Statuses
          ✅  passed
          ⚠️  passed with findings or has a readiness gap
          ❌  failed or incomplete
          ➖  not applicable
          ⏭️  skipped

        """;

    /// <summary>
    /// Usage followed by the shipped check and group vocabulary, so the identifiers
    /// accepted by --only, --skip and explain are discoverable from the tool itself.
    /// </summary>
    public static string For(IReadOnlyList<IRepositoryCheck> checks)
    {
        // Column widths follow the identifiers this build actually ships: a fixed width
        // silently runs a long identifier into the next column, and these lines are parsed.
        var identifier = checks.Max(check => check.Id.Length) + 2;
        var group = checks.Max(check => check.Group.Length) + 2;

        var text = new StringBuilder(Usage);
        text.Append("\nChecks\n");
        foreach (var check in checks)
        {
            text.Append("  ")
                .Append(check.Id.PadRight(identifier))
                .Append("group ")
                .Append(check.Group.PadRight(group))
                .Append(check.Summary)
                .Append('\n');
        }

        return text.ToString();
    }
}
