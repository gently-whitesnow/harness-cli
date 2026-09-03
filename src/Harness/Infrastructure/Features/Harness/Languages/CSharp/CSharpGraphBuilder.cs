using Harness.Languages.CSharp;
using Harness.Structure;

namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>
/// Turns read files into the dependency graph. An edge ends at a type the repository declares;
/// a name resolving nowhere is no edge, and one resolving to several places stays unresolved.
/// </summary>
internal sealed class CSharpGraphBuilder
{
    private readonly IReadOnlyList<CSharpFile> files;
    private readonly IReadOnlyList<string> markedGenerated;
    private readonly Dictionary<(string From, string To), ReferenceEdge> edges = [];
    private readonly List<ExternalImports> imports = [];
    private int resolved;
    private int ambiguous;

    private CSharpGraphBuilder(IReadOnlyList<CSharpFile> files, IReadOnlyList<string> markedGenerated)
    {
        this.files = files;
        this.markedGenerated = markedGenerated;
    }

    public static SourceGraph Build(IReadOnlyList<CSharpFile> files, IReadOnlyList<string> markedGenerated)
        => new CSharpGraphBuilder(files, markedGenerated).Read();

    private SourceGraph Read()
    {
        var declared = files.ToDictionary(
            file => file.Path,
            file => file.Types.Select(type => new DeclaredType(type, NodeOf(file, type))).ToList(),
            StringComparer.Ordinal);

        var nodes = declared.Values.SelectMany(types => types.Select(type => type.Node)).ToList();
        var index = new CSharpTypeIndex(nodes);

        foreach (var file in files)
        {
            Collect(file, declared[file.Path], index);
        }

        return new SourceGraph(
            files.Select(file => file.Path).ToList(),
            nodes,
            edges.Values.OrderBy(edge => edge.Location, StringComparer.Ordinal).ToList(),
            imports,
            resolved,
            ambiguous,
            markedGenerated);
    }

    private static TypeNode NodeOf(CSharpFile file, Declaration type)
        => new(type.Subject, type.Name, type.Module, file.Path, type.FirstLine);

    private void Collect(CSharpFile file, List<DeclaredType> declared, CSharpTypeIndex index)
    {
        var imported = file.Structure.Imports.ToHashSet(StringComparer.Ordinal);
        imports.Add(new ExternalImports(
            file.Path, imported.Count(import => !index.IsInternal(import))));

        var owners = OwnersByLine(file, declared);
        foreach (var occurrence in CSharpReferenceScanner.Scan(file.Source, index.Knows))
        {
            Reference(owners, occurrence, index, imported);
        }

        foreach (var type in declared)
        {
            Inherit(type, index, imported);
        }
    }

    private void Reference(
        IReadOnlyList<TypeNode?> owners,
        NameOccurrence occurrence,
        CSharpTypeIndex index,
        HashSet<string> imported)
    {
        if (OwnerAt(owners, occurrence.Line) is not { } owner)
        {
            return;
        }

        var (target, unresolved) = index.Resolve(occurrence.Name, owner.Module, imported);
        if (unresolved)
        {
            ambiguous++;
            return;
        }

        if (target is null || string.Equals(target.Subject, owner.Subject, StringComparison.Ordinal))
        {
            return;
        }

        resolved++;
        Connect(owner, target, occurrence.Grade, occurrence.Line);
    }

    // A base list names types and nothing else, so what it names is proved by its position.
    private void Inherit(DeclaredType type, CSharpTypeIndex index, HashSet<string> imported)
    {
        foreach (var name in BaseTypeNames(type.Declaration.Header).Where(index.Knows))
        {
            var (target, _) = index.Resolve(name, type.Node.Module, imported);
            if (target is not null && !string.Equals(target.Subject, type.Node.Subject, StringComparison.Ordinal))
            {
                Connect(type.Node, target, EvidenceGrade.Proven, type.Declaration.FirstLine);
            }
        }
    }

    private void Connect(TypeNode from, TypeNode to, EvidenceGrade grade, int line)
    {
        var key = (from.Subject, to.Subject);
        if (!edges.TryGetValue(key, out var existing))
        {
            edges[key] = new ReferenceEdge(from, to, grade, line);
            return;
        }

        // The strongest evidence for a dependency is what the dependency is worth, and the
        // line that carries it is the one worth printing.
        if (grade == EvidenceGrade.Proven && existing.Grade == EvidenceGrade.Inferred)
        {
            edges[key] = new ReferenceEdge(from, to, grade, line);
        }
    }

    private static TypeNode? OwnerAt(IReadOnlyList<TypeNode?> owners, int line)
        => line >= 1 && line <= owners.Count ? owners[line - 1] : null;

    /// <summary>Which type owns each line: a nested type's narrower span wins, so a reference is
    /// attributed to the type that names it.</summary>
    private static TypeNode?[] OwnersByLine(CSharpFile file, List<DeclaredType> declared)
    {
        var owners = new TypeNode?[file.Source.LineCount];
        foreach (var type in declared.OrderByDescending(type => type.Span))
        {
            var last = Math.Min(type.Declaration.LastLine, owners.Length);
            for (var line = type.Declaration.FirstLine; line <= last; line++)
            {
                owners[line - 1] = type.Node;
            }
        }

        return owners;
    }

    private static List<string> BaseTypeNames(string header)
    {
        var names = new List<string>();
        var text = CSharpDeclarationSyntax.BaseListOf(header);
        var depth = 0;
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var character = index < text.Length ? text[index] : ' ';
            if (depth == 0 && (char.IsLetterOrDigit(character) || character is '_' or '@'))
            {
                start = start < 0 ? index : start;
                continue;
            }

            if (start >= 0)
            {
                names.Add(text[start..index]);
                start = -1;
            }

            // An argument list on a base type carries values, not the names of types.
            depth += character == '(' ? 1 : character == ')' ? -1 : 0;
        }

        return names;
    }

    private sealed record DeclaredType(Declaration Declaration, TypeNode Node)
    {
        public int Span => Declaration.LastLine - Declaration.FirstLine;
    }
}
