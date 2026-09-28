namespace Harness.Infrastructure.Languages.TypeScript;

/// <summary>One literal import or reexport; <see cref="Bindings"/> is null when it reaches the whole module.</summary>
internal sealed record TypeScriptImport(string Specifier, int Line, bool TypeOnly, IReadOnlyList<TypeScriptBinding>? Bindings,
    bool Reexport, bool Dynamic);
