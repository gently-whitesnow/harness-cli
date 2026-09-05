using Harness.Languages.CSharp;

namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>
/// Reads declarations out of masked C# by matching braces and handing the text that precedes
/// each one to <see cref="CSharpDeclarationSyntax"/>. This is a lexical reader, not a compiler
/// front end: it recognizes the type declarations the dependency graph needs and treats
/// everything else as an ordinary block.
/// </summary>
internal sealed class CSharpStructureReader
{
    private readonly CSharpSource source;
    private readonly List<Declaration> declarations = [];
    private readonly List<Scope> scopes = [new Scope()];
    private readonly List<string> qualifiers = [];
    private readonly List<string> imports = [];
    private int namespaceDepth;
    private int typeDepth;

    private CSharpStructureReader(CSharpSource source) => this.source = source;

    public static CSharpStructure Read(CSharpSource source)
    {
        var reader = new CSharpStructureReader(source);
        reader.Walk();
        return new CSharpStructure(
            reader.declarations.Where(declaration => declaration.IsComplete).ToList(),
            reader.imports);
    }

    private void Walk()
    {
        var text = source.Masked;
        var headerStart = -1;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is not ('{' or '}' or ';'))
            {
                if (headerStart < 0 && !char.IsWhiteSpace(character))
                {
                    headerStart = index;
                }

                continue;
            }

            var header = headerStart < 0 ? string.Empty : text[headerStart..index];
            var headerIndex = headerStart;
            headerStart = -1;

            switch (character)
            {
                case '{':
                    OpenBrace(header, headerIndex);
                    break;

                case '}':
                    CloseBrace(index);
                    break;

                default:
                    Terminate(header, headerIndex, index);
                    break;
            }
        }
    }

    private void OpenBrace(string header, int headerIndex)
    {
        var (text, offset) = Clean(header);
        var scope = new Scope();

        if (typeDepth == 0 && CSharpDeclarationSyntax.NamespaceNameOf(text) is { } namespaceName)
        {
            scope.QualifiesNamespace = true;
            qualifiers.Add(namespaceName);
            namespaceDepth++;
            scopes.Add(scope);
            return;
        }

        var (declaration, typeName) = Recognize(text, headerIndex + offset);
        scope.Declaration = declaration;

        if (typeName is not null)
        {
            scope.TypeName = typeName;
            qualifiers.Add(typeName);
            typeDepth++;
        }

        scopes.Add(scope);
    }

    private void CloseBrace(int index)
    {
        if (scopes.Count <= 1)
        {
            return;
        }

        var scope = scopes[^1];
        scopes.RemoveAt(scopes.Count - 1);

        if (scope.Declaration is not null)
        {
            scope.Declaration.LastLine = source.LineOf(index);
        }

        if (scope.TypeName is not null)
        {
            typeDepth--;
            qualifiers.RemoveAt(qualifiers.Count - 1);
        }

        if (scope.QualifiesNamespace)
        {
            namespaceDepth--;
            qualifiers.RemoveAt(qualifiers.Count - 1);
        }
    }

    private void Terminate(string header, int headerIndex, int index)
    {
        var (text, offset) = Clean(header);
        if (text.Length == 0)
        {
            return;
        }

        if (typeDepth == 0)
        {
            // A `using` inside a member is a statement, not an import; inside a type there
            // are no directives, so the enclosing type depth is what tells them apart.
            if (CSharpDeclarationSyntax.IsUsingDirective(text))
            {
                Import(text);
                return;
            }

            if (CSharpDeclarationSyntax.NamespaceNameOf(text) is { } namespaceName)
            {
                // A file-scoped namespace qualifies everything that follows it in the file.
                qualifiers.Add(namespaceName);
                namespaceDepth++;
                return;
            }
        }

        // A positional type declaration that ends at a semicolon spans only its own lines.
        var (declaration, _) = Recognize(text, headerIndex + offset);
        if (declaration is not null)
        {
            declaration.LastLine = source.LineOf(index);
        }
    }

    private void Import(string text)
    {
        if (CSharpDeclarationSyntax.ImportNameOf(text) is { } import)
        {
            imports.Add(import);
        }
    }

    private (Declaration? Declaration, string? TypeName) Recognize(string text, int index)
    {
        if (CSharpDeclarationSyntax.TypeOf(text) is { } recognizedType)
        {
            var type = Declare(DeclarationKind.Type, recognizedType.Name, index, text);
            type.TypeForm = recognizedType.Form;
            type.IsNestedType = typeDepth > 0;
            return (type, recognizedType.Name);
        }

        return (null, null);
    }

    private Declaration Declare(
        DeclarationKind kind,
        string name,
        int index,
        string header)
    {
        var declaration = new Declaration
        {
            Kind = kind,
            Subject = Qualify(name),
            Name = name,
            Module = string.Join('.', qualifiers.Take(namespaceDepth)),
            Owner = typeDepth == 0 ? null : Qualify(),
            FirstLine = source.LineOf(index),
            Header = header,
        };

        declarations.Add(declaration);
        return declaration;
    }

    private string Qualify(string? name = null)
        => string.Join('.', name is null ? qualifiers : qualifiers.Append(name));

    private static (string Text, int Offset) Clean(string header)
    {
        var (text, offset) = CSharpDeclarationSyntax.WithoutAttributes(header);
        return (CSharpDeclarationSyntax.WithoutExpressionBody(text), offset);
    }

    private sealed class Scope
    {
        public string? TypeName { get; set; }

        public bool QualifiesNamespace { get; set; }

        public Declaration? Declaration { get; set; }
    }
}
