namespace Harness.Checks.CSharp;

internal sealed class Declaration
{
    public required DeclarationKind Kind { get; init; }

    public required string Subject { get; init; }

    public required int FirstLine { get; init; }

    public int LastLine { get; set; }

    public int ParameterCount { get; set; } = -1;

    public int PublicMembers { get; set; }

    public TypeForm TypeForm { get; set; }

    public bool IsNestedType { get; set; }

    public bool IsComplete => LastLine >= FirstLine;
}
