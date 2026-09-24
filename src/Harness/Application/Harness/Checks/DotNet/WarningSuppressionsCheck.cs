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

    // EditorConfig applies to all supported .NET languages; pragma parsing remains C# only.
    private static readonly EvidenceFile[] EditorConfigSources =
        [Sources, new("*.vb"), new("*.fs"), new("*.fsi")];

    private static readonly EvidenceFile BuildProps = new("Directory.Build.props", Inherited: true);
    private static readonly EvidenceFile BuildTargets = new("Directory.Build.targets", Inherited: true);

    private static readonly EvidenceFile EditorConfig = new(".editorconfig", Inherited: true);

    private static readonly EvidenceFile GlobalConfig = new("*.globalconfig");
    private static readonly EvidenceFile RuleSet = new("*.ruleset");


    private static readonly string[] ProjectProperties = ["NoWarn", "WarningsNotAsErrors"];

    private static readonly HashSet<string> SilencingSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "silent", "suggestion",
    };

    public override string Id => "warning-suppressions.dotnet";

    public override string Group => "warning-suppressions";

    public override string Summary => "no silenced diagnostics outside the allowed list";

    public override string Explanation => WarningSuppressionsExplanation.Text;

    protected override IReadOnlyList<EvidenceFile> PolicyFiles => [.. EditorConfigSources, BuildProps, BuildTargets,
        new EvidenceFile("*.props"), new EvidenceFile("*.targets"), EditorConfig, GlobalConfig, RuleSet];

    protected override CheckEvaluation Inspect(CheckContext context, IReadOnlyList<DotNetFile> projects)
    {
        var sites = new List<Site>();
        var importFindings = new List<Finding>();

        var failure = CollectFromSources(context, sites);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        var buildFailure = CollectFromBuildFiles(context, projects, sites, importFindings);
        if (buildFailure is not null)
        {
            return CheckEvaluation.Incomplete(buildFailure);
        }

        var (generatedSections, analyzerFailure) = CollectFromAnalyzerConfigs(context, sites);
        if (analyzerFailure is not null)
        {
            return CheckEvaluation.Incomplete(analyzerFailure);
        }

        var declared = context.Config!.Settings.RepositoryWideFor(Id);
        var actual = sites.Where(site => site.RepositoryWide && site.Code is not null)
            .Select(site => site.Code!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var findings = sites
            .Where(site => !site.RepositoryWide)
            .Select(site => Block(site.Location, site.Code is null
                ? $"silences every warning via {site.Form}; a rule is switched off by name, for the whole repository"
                : $"silences {site.Code} via {site.Form} at one address; fix the code, or switch {site.Code} off "
                    + "for the whole repository in .editorconfig [*.cs] or Directory.Build.props"))
            .ToList();
        findings.AddRange(importFindings);
        findings.AddRange(generatedSections);
        findings.AddRange(sites.Where(site => site.RepositoryWide
                && (site.Code is null || !declared.ContainsKey(site.Code)))
            .Select(site => Block(site.Location,
                site.Code is null
                    ? $"{site.Form} silences a wildcard or diagnostic category; name individual diagnostics instead"
                    : $"{site.Code} is switched off repository-wide via {site.Form} without an id/reason entry in settings.{Id}.repositoryWide")));
        findings.AddRange(declared.Keys.Where(id => !actual.Contains(id))
            .Select(id => Block(Harness.Config.HarnessConfig.FileName,
                $"stale settings.{Id}.repositoryWide entry for {id}: diagnostic is not switched off")));

        var details = sites
            .Where(site => site.RepositoryWide)
            .OrderBy(site => site.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.Location, StringComparer.Ordinal)
            .Select(site => $"{site.Code} is switched off repository-wide via {site.Form} at {site.Location}; "
                + $"declared reason: {(site.Code is not null && declared.TryGetValue(site.Code, out var reason) ? reason : "undeclared")}")
            .ToList();

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "no diagnostic is silenced at an address" : null,
            details: details);
    }

    private static string? CollectFromBuildFiles(
        CheckContext context, IReadOnlyList<DotNetFile> projects, List<Site> sites, List<Finding> importFindings)
    {
        foreach (var project in projects)
        {
            CollectFromXml(project, sites, repositoryWide: false);
            CollectUnresolvedConfigReferences(project, sites);
            foreach (var imported in BuildPropertiesCheck.ReadLocalImports(context, project, importFindings))
            {
                CollectFromXml(imported, sites, repositoryWide: false);
                CollectUnresolvedConfigReferences(imported, sites);
            }
        }

        // MSBuild imports only the nearest Directory.Build.props above a project.
        var applied = projects
            .Select(project => context.Nearest(BuildProps, project.Path))
            .OfType<TrackedEntry>()
            .DistinctBy(entry => entry.Path)
            .OrderBy(entry => entry.Path, StringComparer.Ordinal);
        foreach (var entry in applied)
        {
            var (props, readFailure) = DotNetRepository.Read(context.Repository, entry);
            if (props is null)
            {
                return readFailure;
            }

            CollectFromXml(props, sites, repositoryWide: projects.All(project =>
                context.Nearest(BuildProps, project.Path)?.Path == entry.Path));
            CollectUnresolvedConfigReferences(props, sites);
            foreach (var imported in BuildPropertiesCheck.ReadLocalImports(context, props, importFindings))
            {
                CollectFromXml(imported, sites, repositoryWide: false);
                CollectUnresolvedConfigReferences(imported, sites);
            }
        }

        var targets = projects.Select(project => context.Nearest(BuildTargets, project.Path))
            .OfType<TrackedEntry>().DistinctBy(entry => entry.Path);
        foreach (var entry in targets)
        {
            var (file, readFailure) = DotNetRepository.Read(context.Repository, entry);
            if (file is null)
            {
                return readFailure;
            }

            CollectFromXml(file, sites, repositoryWide: projects.All(project =>
                context.Nearest(BuildTargets, project.Path)?.Path == entry.Path));
            CollectUnresolvedConfigReferences(file, sites);
            foreach (var imported in BuildPropertiesCheck.ReadLocalImports(context, file, importFindings))
            {
                CollectFromXml(imported, sites, repositoryWide: false);
                CollectUnresolvedConfigReferences(imported, sites);
            }
        }

        return null;
    }

    private static (List<Finding> GeneratedSections, string? Failure) CollectFromAnalyzerConfigs(
        CheckContext context, List<Site> sites)
    {
        var (configs, configFailure) = EditorConfigChain.ReadAll(context, EditorConfig);
        if (configs is null)
        {
            return ([], configFailure);
        }

        var covered = CoveredSources(context, configs);
        var generatedSections = new List<Finding>();
        foreach (var source in EditorConfigSources.SelectMany(context.Tracked).DistinctBy(entry => entry.Path, StringComparer.Ordinal))
        {
            foreach (var file in EditorConfigChain.ChainFor(configs, source.Path))
            {
                var relative = EditorConfigChain.RelativeTo(context.Repository.RootPath, file.Directory, source.Path);
                if (file.Sections.Any(section => section.IsGeneratedCode && EditorConfigGlob.Matches(section.Glob, relative))
                    && context.Repository.Classify(source) != EvidenceKind.DeclaredGenerated)
                {
                    generatedSections.Add(Block(source.Path,
                        "generated_code = true applies to source outside a declared generated path with a toolchain marker"));
                }
            }
        }
        var sourceCount = EditorConfigSources.SelectMany(context.Tracked)
            .DistinctBy(entry => entry.Path, StringComparer.Ordinal).Count();
        foreach (var file in configs)
        {
            var paths = covered.TryGetValue(file, out var addressed) ? addressed : [];
            var sections = file.Sections
                .Where(section => section.Entries.Any(entry => SilencingSeverities.Contains(Severity(entry.Value))))
                .Where(section => paths.Any(path => EditorConfigGlob.Matches(section.Glob, path)))
                .ToList();
            CollectFromEditorConfig(file with { Sections = sections }, sites,
                sourceCount > 0 && paths.Count == sourceCount);
        }

        foreach (var entry in context.Tracked(GlobalConfig))
        {
            var (text, readFailure) = context.Repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], readFailure ?? $"Could not read '{entry.Path}'.");
            }

            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var match = GlobalSeverity().Match(lines[index]);
                if (match.Success)
                {
                    sites.Add(new Site($"{entry.Path}:{index + 1}", Normalize(match.Groups[1].Value),
                        ".globalconfig severity = " + match.Groups[2].Value, false));
                }
            }
        }

        foreach (var entry in context.Tracked(RuleSet))
        {
            var (text, readFailure) = context.Repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], readFailure ?? $"Could not read '{entry.Path}'.");
            }

            if (text.Contains("Action=\"None\"", StringComparison.OrdinalIgnoreCase))
            {
                sites.Add(new Site(entry.Path, null, "ruleset Action=None", false));
            }
        }

        return (generatedSections, null);
    }

    /// <summary>
    /// The sources each .editorconfig is in force for, relative to its directory: a section is
    /// judged in the frame whose tracked sources it addresses. The chain is resolved per directory.
    /// </summary>
    private static Dictionary<EditorConfigFile, List<string>> CoveredSources(
        CheckContext context,
        IReadOnlyList<EditorConfigFile> configs)
    {
        var covered = new Dictionary<EditorConfigFile, List<string>>(ReferenceEqualityComparer.Instance);
        var chains = new Dictionary<string, List<EditorConfigFile>>(StringComparer.Ordinal);
        foreach (var source in EditorConfigSources.SelectMany(context.Tracked))
        {
            var directory = EditorConfigChain.DirectoryOf(source.Path);
            if (!chains.TryGetValue(directory, out var chain))
            {
                chain = EditorConfigChain.ChainFor(configs, source.Path);
                chains[directory] = chain;
            }

            foreach (var file in chain)
            {
                if (!covered.TryGetValue(file, out var paths))
                {
                    paths = [];
                    covered[file] = paths;
                }

                paths.Add(EditorConfigChain.RelativeTo(context.Repository.RootPath, file.Directory, source.Path));
            }
        }

        return covered;
    }

    private static string? CollectFromSources(CheckContext context, List<Site> sites)
    {
        var entries = context.Tracked(Sources)
            .Where(entry => context.Repository.Classify(entry) is not (EvidenceKind.DeclaredGenerated or EvidenceKind.ToolchainIgnored));
        foreach (var entry in entries)
        {
            var (text, failure) = context.Repository.ReadTrackedText(entry);
            if (text is null)
            {
                return failure ?? $"Could not read '{entry.Path}'.";
            }

            CollectPragmas(entry.Path, text, sites);
            CollectAttributes(entry.Path, text, sites);
            CollectNullableDisables(entry.Path, text, sites);
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

    private static void CollectNullableDisables(string path, string text, List<Site> sites)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (NullableDisable().IsMatch(lines[index]))
            {
                sites.Add(new Site($"{path}:{index + 1}", null, "#nullable disable", false));
            }
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
                var conditional = element.AncestorsAndSelf().Any(node =>
                    !string.IsNullOrWhiteSpace(node.Attribute("Condition")?.Value));
                sites.AddRange(Codes(value, ';', ',', ' ', '\n', '\t')
                    .Select(code => new Site(location, code, property, repositoryWide && !conditional)));
                if (value.Replace("$(NoWarn)", string.Empty, StringComparison.Ordinal)
                    .Contains("$(", StringComparison.Ordinal))
                {
                    sites.Add(new Site(location, null, $"unresolved MSBuild property in {property}", false));
                }
            }
        }
    }

    private static void CollectUnresolvedConfigReferences(DotNetFile file, List<Site> sites)
    {
        foreach (var property in new[] { "GlobalAnalyzerConfigFiles", "CodeAnalysisRuleSet" })
        {
            foreach (var element in DotNetRepository.Elements(file, property))
            {
                sites.Add(new Site(file.Path, null, $"{property} requires a reviewed analyzer configuration", false));
            }
        }
    }

    private static void CollectFromEditorConfig(EditorConfigFile file, List<Site> sites, bool appliesToAllSources)
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
                        appliesToAllSources && IsRepositoryWide(section.Glob)));
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

    [GeneratedRegex(@"^\s*#pragma\s+warning\s+disable\b(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Pragma();

    [GeneratedRegex(@"^\s*#nullable\s+disable\b", RegexOptions.CultureInvariant)]
    private static partial Regex NullableDisable();

    [GeneratedRegex(
        @"\[\s*(?:assembly\s*:\s*)?(?:System\.Diagnostics\.CodeAnalysis\.)?(?:Unconditional)?SuppressMessage\s*\(\s*""[^""]*""\s*,\s*""([A-Za-z]+\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SuppressionAttribute();

    [GeneratedRegex(@"^dotnet_diagnostic\.([a-z]+\d+)\.severity$", RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticSeverity();

    [GeneratedRegex(@"dotnet_diagnostic\.([A-Za-z]+\d+)\.severity\s*=\s*(none|silent|suggestion)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GlobalSeverity();

    [GeneratedRegex(@"^\*(\.(\{[A-Za-z0-9,]+\}|[A-Za-z0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryWideGlob();

    private sealed record Site(string Location, string? Code, string Form, bool RepositoryWide);
}
