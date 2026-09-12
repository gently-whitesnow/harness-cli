using Harness.Repository;


namespace Harness.Checks.DotNet;

/// <summary>
/// Every SDK-style project must sit under a tracked `.editorconfig` chain that puts the
/// shared code-style baseline in force for its sources. With EnforceCodeStyleInBuild and
/// warnings-as-errors from Directory.Build.props, these keys are what makes the style a
/// build failure instead of an editor hint.
/// </summary>
internal sealed class EditorConfigCheck : DotNetCheck
{
    private const string SampleFile = "Sample.cs";

    private static readonly EvidenceFile EditorConfig = new(".editorconfig");

    private static readonly IReadOnlyList<KeyValuePair<string, string>> Required =
    [
        new("end_of_line", "lf"),
        new("insert_final_newline", "true"),
        new("indent_style", "space"),
        new("indent_size", "4"),
        new("dotnet_diagnostic.ide0055.severity", "warning"),
        new("dotnet_sort_system_directives_first", "true"),
        new("csharp_using_directive_placement", "outside_namespace"),
        new("dotnet_diagnostic.ide0065.severity", "warning"),
        new("csharp_prefer_braces", "true"),
        new("dotnet_diagnostic.ide0011.severity", "warning"),
        new("csharp_style_namespace_declarations", "file_scoped"),
        new("dotnet_diagnostic.ide0161.severity", "warning"),
        new("dotnet_style_require_accessibility_modifiers", "for_non_interface_members"),
        new("dotnet_diagnostic.ide0040.severity", "warning"),
        new("csharp_style_var_for_built_in_types", "true"),
        new("csharp_style_var_when_type_is_apparent", "true"),
        new("csharp_style_var_elsewhere", "true"),
        new("dotnet_diagnostic.ide0007.severity", "warning"),
        new("csharp_new_line_before_open_brace", "all"),
    ];

    public override string Id => "editorconfig.dotnet";

    public override string Group => "editorconfig";

    public override string Summary => "shared .editorconfig code-style baseline";

    public override string Explanation => EditorConfigExplanation.Text;

    protected override IReadOnlyList<EvidenceFile> PolicyFiles => [EditorConfig];

    protected override CheckEvaluation Inspect(CheckContext context, IReadOnlyList<DotNetFile> projects)
    {
        var (files, failure) = EditorConfigChain.ReadAll(context, EditorConfig);
        if (files is null)
        {
            return CheckEvaluation.Incomplete(failure!);
        }

        var findings = new List<Finding>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            var chain = EditorConfigChain.ChainFor(files, project.Path);
            if (chain.Count == 0)
            {
                findings.Add(Block(project.Path, "is not covered by a tracked .editorconfig"));
                continue;
            }

            var effective = new Dictionary<string, string>(StringComparer.Ordinal);
            var sample = $"{EditorConfigChain.DirectoryOf(project.Path)}/{SampleFile}".TrimStart('/');
            foreach (var file in chain)
            {
                file.ApplyTo(effective, EditorConfigChain.RelativeTo(context.Repository.RootPath, file.Directory, sample));
            }

            var nearest = chain[^1];
            foreach (var expected in Required)
            {
                var message = Judge(effective, expected);
                if (message is not null && reported.Add($"{nearest.Path}|{message}"))
                {
                    findings.Add(Block(nearest.Path, message));
                }
            }
        }

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "every project reads the shared code-style baseline from .editorconfig" : null);
    }

    private static string? Judge(Dictionary<string, string> effective, KeyValuePair<string, string> expected)
    {
        if (!effective.TryGetValue(expected.Key, out var raw))
        {
            return $"must set {expected.Key} = {expected.Value}";
        }

        var actual = Normalize(raw);
        var accepted = string.Equals(actual, expected.Value, StringComparison.Ordinal)
            || (expected.Value == "warning" && actual == "error");
        return accepted ? null : $"sets {expected.Key} to '{raw}' instead of {expected.Value}";
    }

    // `true:warning` is the legacy spelling that carries a severity after the value.
    private static string Normalize(string value)
    {
        var colon = value.IndexOf(':');
        return (colon < 0 ? value : value[..colon]).Trim().ToLowerInvariant();
    }

}
