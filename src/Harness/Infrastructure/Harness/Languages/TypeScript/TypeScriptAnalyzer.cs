using System.Text.RegularExpressions;
using Harness.Languages;
using Harness.Repository;
using Harness.Structure;

namespace Harness.Infrastructure.Languages.TypeScript;

/// <summary>File graph with literal imports and transparent reexport-only barrels.</summary>
internal sealed class TypeScriptAnalyzer(TypeScriptSources sources) : ILanguageAnalyzer
{
    private IRepository? read;
    private (SourceGraph? Graph, string? Failure) result;
    public Language Language => Language.TypeScript;
    public string Unit => "file";
    public string NothingToAnalyze => sources.NothingToAnalyze;
    public IReadOnlyList<string> NamedEvidence => [];

    public (SourceGraph? Graph, string? Failure) ReadGraph(IRepository repository)
    {
        if (!ReferenceEquals(read, repository)) { result = Build(repository); read = repository; }
        return result;
    }

    private static (SourceGraph? Graph, string? Failure) Build(IRepository repository)
    {
        var files = new Dictionary<string, File>(StringComparer.Ordinal);
        foreach (var entry in repository.TrackedEntries.Where(entry => TypeScriptFile.IsSource(entry.Path)
            && repository.Classify(entry) is not (EvidenceKind.DeclaredGenerated or EvidenceKind.ToolchainIgnored)))
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return (null, failure ?? $"Could not read '{entry.Path}'.");
            }

            files[entry.Path] = new File(entry.Path, text, TypeScriptImports.Read(text), TypeScriptFile.IsTest(entry.Path));
        }
        if (files.Count == 0)
        {
            return (new SourceGraph([], [], [], [], 0, 0, [], []), null);
        }

        var resolver = new TypeScriptResolver(repository);
        var authored = files.Values.Where(file => !file.IsTest).ToList();
        var barrels = authored.Where(IsBarrel).ToDictionary(file => file.Path, StringComparer.Ordinal);
        var nodes = authored.Where(file => !barrels.ContainsKey(file.Path))
            .ToDictionary(file => file.Path, file => new TypeNode(file.Path, Path.GetFileName(file.Path), Module(file.Path), file.Path, 1), StringComparer.Ordinal);
        var edges = new Dictionary<(string, string), ReferenceEdge>();
        var unresolved = new List<string>();
        var imports = new List<ExternalImports>();
        var resolved = 0;
        foreach (var file in authored.Where(file => nodes.ContainsKey(file.Path)))
        {
            var externalCount = 0;
            foreach (var import in file.Imports)
            {
                var target = resolver.Resolve(file.Path, import.Specifier, out var external);
                if (external) { externalCount++; continue; }
                if (target is null) { unresolved.Add($"{file.Path}:{import.Line} {import.Specifier}"); continue; }
                resolved++;
                foreach (var leaf in Expand(target, import.ImportedName, barrels, resolver, []))
                {
                    if (!nodes.TryGetValue(leaf, out var to) || leaf == file.Path)
                    {
                        continue;
                    }

                    var key = (file.Path, leaf);
                    edges.TryAdd(key, new ReferenceEdge(nodes[file.Path], to, EvidenceGrade.Proven, import.Line, import.TypeOnly));
                }
            }
            imports.Add(new ExternalImports(file.Path, externalCount));
        }
        var details = new List<string>();
        details.Add($"resolved literal imports: {resolved}; unresolved: {unresolved.Count}; transparent barrels: {barrels.Count}");
        details.AddRange(unresolved.Take(20).Select(item => "unresolved import: " + item));
        if (unresolved.Count > 20)
        {
            details.Add($"and {unresolved.Count - 20} more unresolved imports");
        }

        details.AddRange(resolver.Failures.Select(item => "configuration: " + item));
        if (resolver.Failures.Count > 0 && unresolved.Any(item => !item.Contains(" ./", StringComparison.Ordinal)
            && !item.Contains(" ../", StringComparison.Ordinal) && !item.Contains(" <dynamic>", StringComparison.Ordinal)))
        {
            return (null, $"TypeScript module resolution is incomplete: {string.Join("; ", resolver.Failures)}");
        }

        return (new SourceGraph(nodes.Keys.Order(StringComparer.Ordinal).ToList(),
            nodes.Values.OrderBy(node => node.Path, StringComparer.Ordinal).ToList(),
            edges.Values.OrderBy(edge => edge.From.Path, StringComparer.Ordinal).ThenBy(edge => edge.To.Path, StringComparer.Ordinal).ToList(),
            imports, resolved, unresolved.Count, [], [])
        { Details = details }, null);
    }

    private static IEnumerable<string> Expand(string path, string? name,
        Dictionary<string, File> barrels, TypeScriptResolver resolver, HashSet<string> visited)
    {
        if (!barrels.TryGetValue(path, out var barrel)) { yield return path; yield break; }
        if (!visited.Add(path))
        {
            yield break;
        }

        var targets = new List<(string Path, string? Name)>();
        foreach (var import in barrel.Imports.Where(import => import.Reexport))
        {
            if (name is not null && import.ImportedName is not null && import.ImportedName != name)
            {
                continue;
            }

            var target = resolver.Resolve(path, import.Specifier, out _);
            if (target is not null)
            {
                targets.Add((target, import.SourceName ?? name));
            }
        }
        foreach (Match export in LocalReexport.Matches(TypeScriptMask.Apply(barrel.Text).Masked))
        {
            var local = export.Groups["local"].Value;
            var exposed = export.Groups["alias"].Success ? export.Groups["alias"].Value : local;
            if (name is not null && exposed != name)
            {
                continue;
            }

            foreach (var import in barrel.Imports.Where(import => !import.Reexport && import.LocalName == local))
            {
                if (resolver.Resolve(path, import.Specifier, out _) is { } target)
                {
                    targets.Add((target, import.SourceName));
                }
            }
        }
        foreach (var target in targets.Distinct())
        {
            foreach (var leaf in Expand(target.Path, target.Name, barrels, resolver, visited))
            {
                yield return leaf;
            }
        }

        visited.Remove(path);
    }

    private static bool IsBarrel(File file)
    {
        if (file.IsTest)
        {
            return false;
        }

        var (masked, _) = TypeScriptMask.Apply(file.Text);
        var skeleton = Regex.Replace(masked,
            @"(?m)^\s*(?:import\b(?:\{[^}]*\}|[^;\n])*|export\s+(?:type\s+)?(?:\*|\{[^}]*\})\s+from\b[^;\n]*)(?:;|$)",
            "", RegexOptions.CultureInvariant);
        foreach (Match export in LocalReexport.Matches(masked))
        {
            if (file.Imports.Any(import => !import.Reexport && import.LocalName == export.Groups["local"].Value))
            {
                skeleton = skeleton.Replace(export.Value, "", StringComparison.Ordinal);
            }
        }

        if (!file.Imports.Any(import => import.Reexport) && LocalReexport.Count(masked) == 0)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(skeleton);
    }

    private static readonly Regex LocalReexport = new(@"(?m)^\s*export(?:\s+type)?\s*\{\s*(?:type\s+)?(?<local>[$\w]+)(?:\s+as\s+(?<alias>[$\w]+))?\s*\}\s*;?", RegexOptions.CultureInvariant);

    private static string Module(string path)
    {
        var split = path.LastIndexOf('/');
        return split < 0 ? "." : path[..split];
    }

    private sealed record File(string Path, string Text, IReadOnlyList<TypeScriptImport> Imports, bool IsTest);
}
