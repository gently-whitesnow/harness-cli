using System.Text;
using Harness.Checks;

namespace Harness.Cli;

internal static class UsageText
{
    private const string Usage =
        """
        harness — repository quality harness

        Usage
          harness check [path] [--only <ids>] [--skip <ids>]
          harness explain <check-id>
          harness help

        Options
          --only <ids>   run only the given check or group identifiers (comma separated)
          --skip <ids>   exclude the given check or group identifiers; they stay visible in the summary

        Exit codes
          0  every selected applicable blocking check completed and passed
          1  a selected applicable blocking check proved a violation
          2  verification could not be completed reliably

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
