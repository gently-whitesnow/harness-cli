namespace Harness.Languages.CSharp;

internal sealed record CSharpFile(CSharpSource Source, CSharpStructure Structure)
{
    public string Path => Source.Path;

    public IEnumerable<Declaration> Types
        => Structure.Declarations.Where(declaration => declaration.Kind == DeclarationKind.Type);
}
