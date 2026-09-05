using System.Text.RegularExpressions;
using System.Xml;
using Harness.Repository;

namespace Harness.Checks.DotNet;

/// <summary>
/// Warnings-as-errors only means something while nobody silences the warnings. This check
/// reads every place a .NET repository can do that and applies the ADR-0035 rule to the
/// compiler's diagnostics: silencing a rule at an address — a file, a project, a path — is
/// blocking; switching a rule off for the whole repository is a decision the report prints.
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
        var sites = new List<Site>();

        var failure = CollectFromSources(context, sites);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        foreach (var project in projects)
        {
            CollectFromXml(project, sites, repositoryWide: false);
        }

        foreach (var entry in context.Tracked(BuildProps))
        {
            var (props, readFailure) = DotNetRepository.Read(context.Repository, entry);
            if (props is null)
            {
                return CheckEvaluation.Incomplete(readFailure!);
            }

            CollectFromXml(props, sites, repositoryWide: true);
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
            .Where(site => !site.RepositoryWide)
            .Select(site => Block(site.Location, site.Code is null
                ? $"silences every warning via {site.Form}; a rule is switched off by name, for the whole repository"
                : $"silences {site.Code} via {site.Form} at one address; fix the code, or switch {site.Code} off "
                    + "for the whole repository in .editorconfig [*.cs] or Directory.Build.props"))
            .ToList();

        var observations = sites
            .Where(site => site.RepositoryWide)
            .OrderBy(site => site.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.Location, StringComparer.Ordinal)
            .Select(site => $"{site.Code} is switched off repository-wide via {site.Form} at {site.Location}")
            .ToList();

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "no diagnostic is silenced at an address" : null,
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
                sites.Add(new Site($"{path}:{index + 1}", null, "#pragma warning disable", false));
            }

            sites.AddRange(codes.Select(code =>
                new Site($"{path}:{index + 1}", code, "#pragma warning disable", false)));
        }
    }

    private static void CollectAttributes(string path, string text, List<Site> sites)
    {
        foreach (Match match in SuppressionAttribute().Matches(text))
        {
            var line = text.AsSpan(0, match.Index).Count('\n') + 1;
            sites.Add(new Site($"{path}:{line}", Normalize(match.Groups[1].Value), "SuppressMessage", false));
        }
    }

    // NoWarn in Directory.Build.props switches a rule off for every project it covers; the
    // same element in one .csproj is that project's private exception.
    private static void CollectFromXml(DotNetFile file, List<Site> sites, bool repositoryWide)
    {
        foreach (var property in ProjectProperties)
        {
            foreach (var element in DotNetRepository.Elements(file, property))
            {
                var line = element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
                var location = line > 0 ? $"{file.Path}:{line}" : file.Path;
                var value = DotNetRepository.Value(element) ?? string.Empty;
                sites.AddRange(Codes(value, ';', ',', ' ', '\n', '\t')
                    .Select(code => new Site(location, code, property, repositoryWide)));
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
                    sites.Add(new Site(
                        location,
                        Normalize(diagnostic.Groups[1].Value),
                        $"[{section.Glob}] severity = {entry.Value}",
                        IsRepositoryWide(section.Glob)));
                }
                else if (entry.Key.StartsWith("dotnet_analyzer_diagnostic.", StringComparison.Ordinal)
                    && entry.Key.EndsWith(".severity", StringComparison.Ordinal))
                {
                    sites.Add(new Site(location, null, $"{entry.Key} = {entry.Value}", false));
                }
            }
        }
    }

    // A section addresses the whole repository when its glob names every file or every file
    // of an extension: `*`, `*.cs`, `*.{cs,vb}`. Anything with a path or a name prefix is an
    // address.
    private static bool IsRepositoryWide(string glob)
        => RepositoryWideGlob().IsMatch(glob.Trim());

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

    [GeneratedRegex(@"^\*(\.(\{[A-Za-z0-9,]+\}|[A-Za-z0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryWideGlob();

    private sealed record Site(string Location, string? Code, string Form, bool RepositoryWide);
}
