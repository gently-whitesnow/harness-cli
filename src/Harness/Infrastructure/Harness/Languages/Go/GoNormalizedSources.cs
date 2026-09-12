using Harness.Languages;
using Harness.Languages.Duplication;
using Harness.Languages.Go;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.Go;

/// <summary>
/// The Go side of the duplication check: the Go mask, and the words the language reserves
/// together with its predeclared identifiers, which no author can rename and which therefore
/// shape a line the way a keyword does.
/// </summary>
internal sealed class GoNormalizedSources(IGoSources sources) : INormalizedSources
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for",
        "func", "go", "goto", "if", "import", "interface", "map", "package", "range", "return", "select",
        "struct", "switch", "type", "var",
        "any", "bool", "byte", "comparable", "complex64", "complex128", "error", "float32", "float64",
        "int", "int8", "int16", "int32", "int64", "rune", "string", "uint", "uint8", "uint16", "uint32",
        "uint64", "uintptr",
        "true", "false", "iota", "nil",
        "append", "cap", "clear", "close", "complex", "copy", "delete", "imag", "len", "make", "max", "min",
        "new", "panic", "print", "println", "real", "recover",
    };

    public Language Language => Language.Go;

    public string NothingToAnalyze => IGoSources.NothingToAnalyze;

    public (IReadOnlyList<NormalizedSource> Files, string? Failure) Read(IRepository repository)
    {
        var (files, failure) = sources.Read(repository);
        return (
            files.Select(file => new NormalizedSource(file.Path, LineTokenizer.Read(file.Masked, file.Regions, Keywords)))
                .ToList(),
            failure);
    }
}
