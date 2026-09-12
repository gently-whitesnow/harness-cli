using System.Globalization;
using System.Text;
using Harness.Languages;

namespace Harness.Config;

/// <summary>
/// Renders the applicability, settings and policy sections of the frame for a set of checks;
/// one renderer serves `init`, `harness.coverage` and `upgrade`, so a printed fragment is
/// exactly what `init` would have written.
/// </summary>
internal static class FrameSections
{
    /// <summary>The axes of application code; a frame without one belongs to a configuration repository.</summary>
    public static readonly IReadOnlyList<string> ApplicationAxes = [Language.CSharp.Key, Language.TypeScript.Key, Language.Go.Key];

    // Calibrated on one configuration repository: visible, not blocking, until more pilots move them.
    private static readonly string[] AdvisoryUntilCalibrated = ["secrets.ansible", "role-shape.ansible"];

    public static bool HasApplicationLanguage(IEnumerable<string> axisKeys)
        => axisKeys.Any(key => ApplicationAxes.Contains(key, StringComparer.Ordinal));

    public static string DefaultPolicy(string checkId, bool applicationLanguage)
        => checkId == "frame.verify"
            ? "required"
            : checkId.StartsWith($"{HarnessConfig.FrameGroup}.", StringComparison.Ordinal)
                ? "off"
                : AdvisoryUntilCalibrated.Contains(checkId, StringComparer.Ordinal)
                    || (checkId == Language.Yaml.Qualify(HarnessSettings.CommentsGroup) && !applicationLanguage)
                    ? "advisory"
                    : "required";

    /// <summary>The default settings section of a check, as JSON lines, or null when it has none.</summary>
    public static string? DefaultSettings(CheckDescriptor check, CommitSettings? commits = null)
    {
        if (check.Group == HarnessSettings.CommentsGroup)
        {
            var comments = CommentSettings.Default;
            return $$"""
                "{{check.Id}}": {
                  "minimumCommentLines": {{comments.MinimumCommentLines}},
                  "percentageLimit": {{comments.PercentageLimit}}
                }
                """;
        }

        if (check.Group == HarnessSettings.DuplicationGroup)
        {
            var duplication = DuplicationSettings.Default;
            return $$"""
                "{{check.Id}}": {
                  "windowLines": {{duplication.WindowLines}},
                  "minimumTokens": {{duplication.MinimumTokens}}
                }
                """;
        }

        if (check.Group == HarnessSettings.ComplexityGroup)
        {
            var complexity = ComplexitySettings.Default;
            return $$"""
                "{{check.Id}}": {
                  "averageReachableFiles": {{complexity.AverageReachableFiles.ToString("0.0", CultureInfo.InvariantCulture)}},
                  "largestCyclicGroupSize": {{complexity.LargestCyclicGroupSize}}
                }
                """;
        }

        return null;
    }

    public static string CommitsSettings(CommitSettings commits)
        => $$"""
            "commits": {
              "language": "{{commits.Code}}",
              "requireSetup": {{(commits.RequireSetup ? "true" : "false")}}
            }
            """;

    public static string ApplicabilityEntry(FrameAxis axis)
        => $"\"{axis.Key}\": {{ \"applicable\": true }}";

    public static string PolicyEntry(CheckDescriptor check, bool applicationLanguage)
        => $"\"{check.Id}\": \"{DefaultPolicy(check.Id, applicationLanguage)}\"";

    /// <summary>The fragment that adds one axis to a frame, indented for pasting into the three objects.</summary>
    public static string AxisFragment(
        FrameAxis axis,
        IReadOnlyList<CheckDescriptor> checks,
        bool architectureMissing,
        bool applicationLanguage)
    {
        var members = checks.Where(check => check.Applicability == axis.Key).ToList();
        var text = new StringBuilder();
        text.Append("\"applicability\": {\n  ").Append(ApplicabilityEntry(axis)).Append("\n}\n");
        var settings = members.Select(check => DefaultSettings(check)).Where(section => section is not null).ToList();
        if (settings.Count > 0)
        {
            text.Append("\"settings\": {\n").Append(Indent(string.Join(",\n", settings!), "  ")).Append("\n}\n");
        }

        text.Append("\"policy\": {\n")
            .Append(Indent(string.Join(",\n", members.Select(check => PolicyEntry(check, applicationLanguage))), "  "))
            .Append("\n}");
        if (axis.Key == "csharp" && architectureMissing)
        {
            text.Append('\n').Append(ArchitectureFragment(checks));
        }

        return text.ToString();
    }

    public static string ArchitectureFragment(IReadOnlyList<CheckDescriptor> checks)
    {
        var policy = checks.Where(check => check.Group == "architecture.sliced-dotnet").Select(check => PolicyEntry(check, applicationLanguage: true));
        return "Choose the repository kind, as in `harness init --kind`:\n"
            + "  application: \"architecture\": { \"standard\": \"sliced-dotnet/1\" }\n"
            + "  library: \"architecture\": { \"applicable\": false, \"reason\": \"standalone library\" }\n"
            + "For either kind, add these checks to policy:\n\"policy\": {\n"
            + Indent(string.Join(",\n", policy), "  ") + "\n}";
    }

    public static string Indent(string text, string prefix)
        => string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? line : prefix + line));
}
