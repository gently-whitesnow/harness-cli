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
                foreach (var leaf in Leaves(target, import, barrels, resolver))
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

    /// <summary>
    /// Files one import reaches: each named specifier through barrels to the module that declares it; the whole
    /// barrel only for namespace or star forms, or when a name is not found among its reexports.
    /// </summary>
    private static IEnumerable<string> Leaves(string target, TypeScriptImport import,
        Dictionary<string, File> barrels, TypeScriptResolver resolver)
    {
        if (import.Bindings is null)
        {
            return Expand(target, null, barrels, resolver, []);
        }

        var leaves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in import.Bindings)
        {
            var named = Expand(target, binding.Source, barrels, resolver, []).ToList();
            leaves.UnionWith(named.Count > 0 ? named : Expand(target, null, barrels, resolver, []));
        }

        return leaves;
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
            var names = new List<string?>();
            if (import.Bindings is null)
            {
                names.Add(name);
            }
            else
            {
                names.AddRange(import.Bindings.Where(binding => name is null || binding.Exposed == name)
                    .Select(binding => name is null ? null : binding.Source));
            }

            if (names.Count > 0 && resolver.Resolve(path, import.Specifier, out _) is { } target)
            {
                targets.AddRange(names.Select(item => (target, item)));
            }
        }
        foreach (var (local, exposed) in LocalReexports(barrel))
        {
            if (name is not null && exposed != name)
            {
                continue;
            }

            foreach (var import in barrel.Imports.Where(import => !import.Reexport))
            {
                foreach (var binding in import.Bindings?.Where(binding => binding.Local == local) ?? [])
                {
                    if (resolver.Resolve(path, import.Specifier, out _) is { } target)
                    {
                        targets.Add((target, name is null ? null : binding.Source));
                    }
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

    /// <summary>`export { A, B as C }` without `from`: each local binding with the name it is exposed under.</summary>
    private static IEnumerable<(string Local, string Exposed)> LocalReexports(File file)
    {
        foreach (Match export in LocalReexport.Matches(TypeScriptMask.Apply(file.Text).Masked))
        {
            foreach (var item in TypeScriptImports.ReadList(export.Groups["list"].Value) ?? [])
            {
                yield return (item.Name, item.Alias);
            }
        }
    }

    private static bool IsBarrel(File file)
    {
        if (file.IsTest)
        {
            return false;
        }

        var (masked, _) = TypeScriptMask.Apply(file.Text);
        var skeleton = Regex.Replace(masked,
            @"(?m)^\s*(?:import\b(?:\{[^}]*\}|[^;\n])*|export\s+(?:type\s+)?(?:\*(?:\s*as\s+[$\w]+)?|\{[^}]*\})\s+from\b[^;\n]*)(?:;|$)",
            "", RegexOptions.CultureInvariant);
        var locals = file.Imports.Where(import => !import.Reexport)
            .SelectMany(import => import.Bindings ?? []).Select(binding => binding.Local).ToHashSet(StringComparer.Ordinal);
        foreach (Match export in LocalReexport.Matches(masked))
        {
            if (TypeScriptImports.ReadList(export.Groups["list"].Value) is [_, ..] list && list.All(item => locals.Contains(item.Name)))
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

    private static readonly Regex LocalReexport = new(@"(?m)^\s*export(?:\s+type)?\s*\{(?<list>[^}]*)\}(?!\s*from\b)\s*;?", RegexOptions.CultureInvariant);

    private static string Module(string path)
    {
        var split = path.LastIndexOf('/');
        return split < 0 ? "." : path[..split];
    }

    private sealed record File(string Path, string Text, IReadOnlyList<TypeScriptImport> Imports, bool IsTest);
}
