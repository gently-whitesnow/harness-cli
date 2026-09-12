using Harness.Languages;
using Harness.Languages.Ansible;
using Harness.Repository;
using Harness.Structure;

namespace Harness.Infrastructure.Languages.Ansible;

/// <summary>
/// The Ansible side of the structural checks. The node is a role under `roles/`; an edge is a
/// role naming another in `meta/main.yml` `dependencies:` or through `include_role` /
/// `import_role` with a literal `name:` in its tasks or handlers. A name written in Jinja is
/// resolved at run time and cannot be an edge; a role outside `roles/` is a collection or
/// galaxy role and stays external. Playbooks are not nodes: they compose roles, they are not one.
/// </summary>
internal sealed class AnsibleAnalyzer(IAnsibleSources sources) : ILanguageAnalyzer
{
    private static readonly string[] IncludeKeys =
        ["include_role", "import_role", "ansible.builtin.include_role", "ansible.builtin.import_role"];

    private static readonly string[] EdgeParts = ["tasks", "handlers"];

    private IRepository? read;
    private (SourceGraph? Graph, string? Failure) result;

    public Language Language => Language.Ansible;

    public string Unit => "role";

    public string NothingToAnalyze => IAnsibleSources.NothingToAnalyze + ", or no tracked role under roles/";

    public IReadOnlyList<string> NamedEvidence => ["ansible.cfg", "*.yml", "*.yaml"];

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

        var roles = sources.RolePaths(repository)
            .Select(path => path.Split('/'))
            .Where(segments => segments.Length > 2)
            .Select(segments => segments[1])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToDictionary(name => name, name => new TypeNode(name, name, name, $"{AnsibleMarkers.RolesDirectory}/{name}", 1), StringComparer.Ordinal);

        var edges = new Dictionary<(string, string), ReferenceEdge>();
        var external = roles.Keys.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
        var resolved = 0;
        var ambiguous = 0;
        foreach (var file in files.Where(file => file.Role is { } role && roles.ContainsKey(role)))
        {
            foreach (var (target, line, literal) in References(file))
            {
                if (!literal)
                {
                    ambiguous++;
                    continue;
                }

                if (!roles.TryGetValue(target, out var to))
                {
                    external[file.Role!]++;
                    continue;
                }

                resolved++;
                var from = new TypeNode(file.Role!, file.Role!, file.Role!, file.Path, line);
                edges.TryAdd((from.Subject, to.Subject), new ReferenceEdge(from, to, EvidenceGrade.Proven, line));
            }
        }

        return (new SourceGraph(
            roles.Values.Select(node => node.Path).ToList(),
            roles.Values.ToList(),
            edges.Values.OrderBy(edge => edge.From.Subject, StringComparer.Ordinal).ThenBy(edge => edge.To.Subject, StringComparer.Ordinal).ToList(),
            roles.Values.Select(node => new ExternalImports(node.Path, external[node.Subject])).ToList(),
            resolved,
            ambiguous,
            [],
            []), null);
    }

    /// <summary>Every role this file names: from `dependencies:` in meta, from role includes in tasks and handlers.</summary>
    private static IEnumerable<(string Target, int Line, bool Literal)> References(AnsibleFile file)
    {
        var lines = file.Lines;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (file.RolePart == "meta" && file.Path.Split('/').Length == 4 && file.Path.Split('/')[^1] is "main.yml" or "main.yaml" && line.Key == "dependencies" && line.Indent == 0)
            {
                var items = Below(lines, index).Where(item => item.IsItem).ToList();
                var depth = items.Count == 0 ? -1 : items.Min(item => item.Indent);
                foreach (var item in items.Where(item => item.Indent == depth && item.Key is null or "role"))
                {
                    yield return (item.Scalar, item.Number, !item.IsJinja);
                }
            }
            else if (EdgeParts.Contains(file.RolePart) && line.Key is { } key && IncludeKeys.Contains(key, StringComparer.Ordinal))
            {
                var name = Below(lines, index).FirstOrDefault(child => child.Key == "name");
                if (name is not null)
                {
                    yield return (name.Scalar, name.Number, !name.IsJinja);
                }
            }
        }
    }

    // The lines nested under a key: those deeper than it, up to the next line at its depth or above.
    private static IEnumerable<AnsibleLine> Below(IReadOnlyList<AnsibleLine> lines, int index)
    {
        var depth = lines[index].Indent;
        for (var next = index + 1; next < lines.Count && (lines[next].Indent > depth || lines[next].IsComment); next++)
        {
            yield return lines[next];
        }
    }
}
