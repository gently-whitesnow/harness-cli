namespace Harness.Languages.CSharp;

internal enum DeclarationKind
{
    Type,

    Constructor,

    Method,

    /// <summary>A field or a property: the state a type holds, under one name.</summary>
    Field,
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

    /// <summary>The member computes its result rather than holding one.</summary>
    public bool HasExpressionBody { get; init; }

    public int LastLine { get; set; }

    public int ParameterCount { get; set; } = -1;

    public int PublicMembers { get; set; }

    public TypeForm TypeForm { get; set; }

    public bool IsNestedType { get; set; }

    public bool IsComplete => LastLine >= FirstLine;
}
