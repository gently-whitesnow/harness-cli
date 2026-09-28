namespace Harness.Infrastructure.Languages.TypeScript;

/// <summary>
/// One named specifier: <see cref="Exposed"/> is the name a barrel lookup matches, <see cref="Source"/> the name read
/// from the target module (null for `export * as ns`, which exposes the whole module) and <see cref="Local"/> the
/// binding an import introduces.
/// </summary>
internal sealed record TypeScriptBinding(string Exposed, string? Source, string? Local, bool TypeOnly);
