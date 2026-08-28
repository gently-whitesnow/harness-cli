namespace Harness.Languages.CSharp;

internal enum DeclarationKind
{
    Type,
}

internal enum TypeForm
{
    Other,
    Class,
    Record,
}

internal sealed class Declaration
{
    public required DeclarationKind Kind { get; init; }

    public required string Subject { get; init; }

    public required string Name { get; init; }

    public required string Module { get; init; }

    public required int FirstLine { get; init; }

    public string? Owner { get; init; }

    public string Header { get; init; } = string.Empty;

    public int LastLine { get; set; }

    public TypeForm TypeForm { get; set; }

    public bool IsNestedType { get; set; }

    public bool IsComplete => LastLine >= FirstLine;
}
