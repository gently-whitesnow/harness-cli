using System.Text;

namespace Harness.Report;

internal static class UsageText
{
    private const string Usage =
        """
        harness — repository quality CLI

        Usage
          harness check [path] [--project <path>] [--only <ids>] [--skip <ids>] [--verbose] [--all]
          harness init [path] [--kind <application|library>] [--languages <keys>] [--latest] [--language <en|ru>]
          harness upgrade [path] [--dry-run]
          harness setup [path]
          harness commit-message check <message-file>
          harness commit-message template
          harness commits check <base>..<head>
          harness explain <check-id>
          harness version
          harness help

        Options
          --project      check one registered project; validates every frame and reports a partial run
          --only <ids>   run one check, or the given check/group identifiers (comma separated)
          --skip <ids>   exclude the given check or group identifiers; they stay visible in the summary
          --verbose      show findings, neutral details, reasons and timings
          --all          show every measured subject instead of top findings; implies --verbose
          --kind         repository kind for non-interactive initialization; asked only when C# is in the index
          --languages    applicability keys to declare instead of detecting them from the index (comma separated)
          --latest       make an initialized frame follow the installed binary's contract
          --dry-run      describe the contract migration without writing changes
          --language     language for human-written commit subjects and bodies (default: ru)

        Frame
          A check the policy does not name is outside the frame and does not run; a check it
          names carries required, advisory or off and its settings section in full.
          harness.coverage reports a tracked language or stack the frame has not decided about.

        Workspace
          Root projects registers disjoint directories with tracked .harness.json files.
          Only the root declares version and commit settings; project frames are explicit.
          check covers the root remainder and every project, regardless of working directory.
          Cross-project duplication and dependency graphs are not measured.

        Architecture selectors
          architecture and architecture.sliced-dotnet select all eight architecture checks.
          Policy declares each individual check ID; group policy is not accepted.

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
    public static string For(IReadOnlyList<CheckSummary> checks)
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
