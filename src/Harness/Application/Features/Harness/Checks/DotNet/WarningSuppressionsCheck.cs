using System.Text.RegularExpressions;
using System.Xml;
using Harness.Config;
using Harness.Repository;

namespace Harness.Checks.DotNet;

/// <summary>
/// Warnings-as-errors only means something while nobody silences the warnings. This check
/// reads every place a .NET repository can do that — pragmas, suppression attributes,
/// NoWarn, editorconfig severities — and blocks each code the frame has not allowed with a
/// reason, repository-wide.
/// </summary>
internal sealed partial class WarningSuppressionsCheck : DotNetCheck
{
    private static readonly EvidenceFile Sources = new("*.cs");

    private static readonly EvidenceFile BuildProps = new("Directory.Build.props");

    private static readonly EvidenceFile EditorConfig = new(".editorconfig");

    private static readonly string[] GeneratedSuffixes = [".g.cs", ".generated.cs", ".designer.cs"];

    private static readonly string[] ProjectProperties = ["NoWarn", "WarningsNotAsErrors"];

    private static readonly HashSet<string> SilencingSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "silent", "suggestion",
    };

    public override string Id => "warning-suppressions.dotnet";

    public override string Group => "warning-suppressions";

    public override string Summary => "no silenced diagnostics outside the allowed list";

    public override string Explanation => WarningSuppressionsExplanation.Text;

    protected override IReadOnlyList<EvidenceFile> PolicyFiles => [Sources, BuildProps, EditorConfig];

    protected override CheckEvaluation Inspect(CheckContext context, IReadOnlyList<DotNetFile> projects)
    {
        var settings = context.Config?.Settings.WarningSuppressions ?? WarningSuppressionSettings.Default;
        var sites = new List<Site>();

        var failure = CollectFromSources(context, sites);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        foreach (var project in projects)
        {
            CollectFromXml(project, sites);
        }

        foreach (var entry in context.Tracked(BuildProps))
        {
            var (props, readFailure) = DotNetRepository.Read(context.Repository, entry);
            if (props is null)
            {
                return CheckEvaluation.Incomplete(readFailure!);
            }

            CollectFromXml(props, sites);
        }

        foreach (var entry in context.Tracked(EditorConfig))
        {
            var (file, readFailure) = EditorConfigFile.Read(context.Repository, entry);
            if (file is null)
            {
                return CheckEvaluation.Incomplete(readFailure!);
            }

            CollectFromEditorConfig(file, sites);
        }

        var findings = sites
            .Where(site => site.Code is null || !settings.Allows(site.Code))
            .Select(site => Block(site.Location, site.Code is null
                ? $"silences every warning via {site.Form}; name the codes instead"
                : $"silences {site.Code} via {site.Form}; fix the code, or allow {site.Code} with a reason in "
                    + "settings.warning-suppressions.dotnet"))
            .ToList();

        var observations = sites
            .Where(site => site.Code is not null && settings.Allows(site.Code))
            .GroupBy(site => site.Code!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key} allowed at {group.Count()} site(s): {settings.Allowed[group.Key]}")
            .ToList();

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "no diagnostic is silenced outside the allowed list" : null,
            observations: observations);
    }

    private static string? CollectFromSources(CheckContext context, List<Site> sites)
    {
        var entries = context.Tracked(Sources)
            .Where(entry => !RepositoryLocations.IsGenerated(entry.Path))
            .Where(entry => !GeneratedSuffixes.Any(suffix =>
                entry.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
        foreach (var entry in entries)
        {
            var (text, failure) = context.Repository.ReadTrackedText(entry);
            if (text is null)
            {
                return failure ?? $"Could not read '{entry.Path}'.";
            }

            if (IsGeneratedContent(text))
            {
                continue;
            }

            CollectPragmas(entry.Path, text, sites);
            CollectAttributes(entry.Path, text, sites);
        }

        return null;
    }

    private static void CollectPragmas(string path, string text, List<Site> sites)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var match = Pragma().Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var codes = Codes(match.Groups[1].Value, ',');
            if (codes.Count == 0)
            {
                sites.Add(new Site($"{path}:{index + 1}", null, "#pragma warning disable"));
            }

            sites.AddRange(codes.Select(code => new Site($"{path}:{index + 1}", code, "#pragma warning disable")));
        }
    }

    private static void CollectAttributes(string path, string text, List<Site> sites)
    {
        foreach (Match match in SuppressionAttribute().Matches(text))
        {
            var line = text.AsSpan(0, match.Index).Count('\n') + 1;
            sites.Add(new Site($"{path}:{line}", Normalize(match.Groups[1].Value), "SuppressMessage"));
        }
    }

    private static void CollectFromXml(DotNetFile file, List<Site> sites)
    {
        foreach (var property in ProjectProperties)
        {
            foreach (var element in DotNetRepository.Elements(file, property))
            {
                var line = element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
                var location = line > 0 ? $"{file.Path}:{line}" : file.Path;
                var value = DotNetRepository.Value(element) ?? string.Empty;
                sites.AddRange(Codes(value, ';', ',', ' ', '\n', '\t')
                    .Select(code => new Site(location, code, property)));
            }
        }
    }

    private static void CollectFromEditorConfig(EditorConfigFile file, List<Site> sites)
    {
        foreach (var section in file.Sections.Where(section => !section.IsGeneratedCode))
        {
            foreach (var entry in section.Entries)
            {
                if (!SilencingSeverities.Contains(Severity(entry.Value)))
                {
                    continue;
                }

                var location = $"{file.Path}:{entry.Line}";
                var diagnostic = DiagnosticSeverity().Match(entry.Key);
                if (diagnostic.Success)
                {
                    sites.Add(new Site(location, Normalize(diagnostic.Groups[1].Value), $"severity = {entry.Value}"));
                }
                else if (entry.Key.StartsWith("dotnet_analyzer_diagnostic.", StringComparison.Ordinal)
                    && entry.Key.EndsWith(".severity", StringComparison.Ordinal))
                {
                    sites.Add(new Site(location, null, $"{entry.Key} = {entry.Value}"));
                }
            }
        }
    }

    private static string Severity(string value)
    {
        var colon = value.LastIndexOf(':');
        return (colon < 0 ? value : value[(colon + 1)..]).Trim();
    }

    private static List<string> Codes(string text, params char[] separators)
    {
        var comment = text.IndexOf("//", StringComparison.Ordinal);
        var declared = comment < 0 ? text : text[..comment];
        return declared.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !token.StartsWith("$(", StringComparison.Ordinal))
            .Select(Normalize)
            .ToList();
    }

    // The compiler accepts bare numbers in pragmas; they name the same CS diagnostic.
    private static string Normalize(string code)
        => code.All(char.IsAsciiDigit) ? $"CS{code}" : code.ToUpperInvariant();

    private static bool IsGeneratedContent(string text)
        => text.Split('\n').Take(5).Any(line => line.Contains("<auto-generated", StringComparison.Ordinal));

    [GeneratedRegex(@"^\s*#pragma\s+warning\s+disable\b(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Pragma();

    [GeneratedRegex(
        @"\[\s*(?:System\.Diagnostics\.CodeAnalysis\.)?(?:Unconditional)?SuppressMessage\s*\(\s*""[^""]*""\s*,\s*""([A-Za-z]+\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SuppressionAttribute();

    [GeneratedRegex(@"^dotnet_diagnostic\.([a-z]+\d+)\.severity$", RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticSeverity();

    private sealed record Site(string Location, string? Code, string Form);
}
