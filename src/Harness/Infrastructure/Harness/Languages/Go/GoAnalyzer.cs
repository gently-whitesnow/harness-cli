using System.Text.RegularExpressions;
using Harness.Languages;
using Harness.Languages.Go;
using Harness.Repository;
using Harness.Structure;

namespace Harness.Infrastructure.Languages.Go;

/// <summary>
/// The Go side of the structural checks. The node is the package: files of one package see
/// each other without an import, so a file graph would show nothing inside a package and the
/// package graph is the one the language actually has. An import resolves to a node when it
/// names a package of a tracked go.mod module in this repository; every other import is
/// external. The compiler forbids import cycles, so the graph is a DAG by construction.
/// </summary>
internal sealed partial class GoAnalyzer(IGoSources sources) : ILanguageAnalyzer
{
    public const string ModuleFile = "go.mod";

    private IRepository? read;
    private (SourceGraph? Graph, string? Failure) result;

    public Language Language => Language.Go;

    public string Unit => "package";

    public string NothingToAnalyze => IGoSources.NothingToAnalyze;

    public IReadOnlyList<string> NamedEvidence => [ModuleFile];

    public (SourceGraph? Graph, string? Failure) ReadGraph(IRepository repository)
    {
        if (!ReferenceEquals(read, repository))
        {
            result = Build(repository);
            read = repository;
        }

        return result;
    }

    private (SourceGraph? Graph, string? Failure) Build(IRepository repository)
    {
        var (files, failure) = sources.Read(repository);
        if (failure is not null)
        {
            return (null, failure);
        }

        var authored = files.Where(file => !file.IsTest).ToList();
        if (authored.Count == 0)
        {
            return (new SourceGraph([], [], [], [], 0, 0, sources.MarkedGenerated(repository), sources.MarkedIgnored(repository)), null);
        }

        var (modules, moduleFailure) = ReadModules(repository);
        if (moduleFailure is not null)
        {
            return (null, moduleFailure);
        }

        if (modules.Count == 0)
        {
            return (null, $"no tracked {ModuleFile} names a module path, so Go import paths cannot be resolved to packages; "
                + $"track the {ModuleFile} of every module");
        }

        var nodes = new Dictionary<string, TypeNode>(StringComparer.Ordinal);
        var byDirectory = new Dictionary<string, TypeNode>(StringComparer.Ordinal);
        foreach (var package in authored.GroupBy(file => file.Directory, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var module = modules
                .Where(candidate => package.Key == candidate.Directory
                    || candidate.Directory.Length == 0
                    || package.Key.StartsWith(candidate.Directory + "/", StringComparison.Ordinal))
                .OrderByDescending(candidate => candidate.Directory.Length)
                .FirstOrDefault();
            if (module is null)
            {
                continue;
            }

            var relative = package.Key == module.Directory
                ? string.Empty
                : module.Directory.Length == 0 ? package.Key : package.Key[(module.Directory.Length + 1)..];
            var importPath = relative.Length == 0 ? module.Path : $"{module.Path}/{relative}";
            var node = new TypeNode(importPath, package.First().Package, module.Path, package.Key.Length == 0 ? "." : package.Key, 1);
            nodes[importPath] = node;
            byDirectory[package.Key] = node;
        }

        var edges = new Dictionary<(string, string), ReferenceEdge>();
        var imports = new List<ExternalImports>();
        var resolved = 0;
        foreach (var package in authored.GroupBy(file => file.Directory, StringComparer.Ordinal))
        {
            if (!byDirectory.TryGetValue(package.Key, out var from))
            {
                continue;
            }

            var external = 0;
            foreach (var file in package)
            {
                foreach (var import in file.Imports)
                {
                    if (!nodes.TryGetValue(import.ImportPath, out var to))
                    {
                        external++;
                        continue;
                    }

                    resolved++;
                    if (to.Subject != from.Subject && !edges.ContainsKey((from.Subject, to.Subject)))
                    {
                        edges[(from.Subject, to.Subject)] = new ReferenceEdge(from, to, EvidenceGrade.Proven, import.Line);
                    }
                }
            }

            imports.Add(new ExternalImports(from.Path, external));
        }

        return (new SourceGraph(
            byDirectory.Values.Select(node => node.Path).Order(StringComparer.Ordinal).ToList(),
            byDirectory.Values.OrderBy(node => node.Path, StringComparer.Ordinal).ToList(),
            edges.Values.OrderBy(edge => edge.From.Path, StringComparer.Ordinal).ThenBy(edge => edge.To.Path, StringComparer.Ordinal).ToList(),
            imports,
            resolved,
            0,
            sources.MarkedGenerated(repository),
            sources.MarkedIgnored(repository)), null);
    }

    private static (List<GoModule> Modules, string? Failure) ReadModules(IRepository repository)
    {
        var modules = new List<GoModule>();
        foreach (var entry in repository.TrackedEntries
            .Where(entry => !entry.IsSymbolicLink && GoSources.IsAuthoredLocation(entry.Path))
            .Where(entry => entry.Path == ModuleFile || entry.Path.EndsWith("/" + ModuleFile, StringComparison.Ordinal)))
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], failure ?? $"Could not read '{entry.Path}'.");
            }

            var path = text.Split('\n')
                .Select(line => ModuleDirective().Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["path"].Value)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(path))
            {
                return ([], $"'{entry.Path}' declares no module path; the Go analyzer cannot resolve imports without one");
            }

            var separator = entry.Path.LastIndexOf('/');
            modules.Add(new GoModule(separator < 0 ? string.Empty : entry.Path[..separator], path));
        }

        return (modules, null);
    }

    [GeneratedRegex("""^[ \t]*module[ \t]+(?:"(?<path>[^"\\\r\n]+)"|(?<path>[^\s"\\]+?))[ \t]*(?://[^\r\n]*)?\r?$""", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleDirective();

    private sealed record GoModule(string Directory, string Path);
}
