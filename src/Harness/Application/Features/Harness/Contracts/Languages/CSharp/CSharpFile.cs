namespace Harness.Languages.CSharp;

/// <summary>
/// One tracked C# file, read once. The declarations are parsed on first ask, so a run that
/// only measures text does not pay for structure nobody looked at.
/// </summary>
internal sealed class CSharpFile(CSharpSource source, Func<CSharpStructure> readStructure)
{
    private CSharpStructure? structure;

    public CSharpSource Source => source;

    public string Path => source.Path;

    public CSharpStructure Structure => structure ??= readStructure();

    public IEnumerable<Declaration> Types
        => Structure.Declarations.Where(declaration => declaration.Kind == DeclarationKind.Type);
}
