using Harness.Languages;
using Harness.Languages.CSharp;
using Harness.Languages.Duplication;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>
/// The C# side of the duplication check: the shared C# mask, and the words C# reserves so
/// that `if` and `while` never read alike while every identifier reads as `n`.
/// </summary>
internal sealed class CSharpNormalizedSources(ICSharpSources sources) : INormalizedSources
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
        "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while",
        "and", "async", "await", "file", "global", "init", "nameof", "not", "or", "record", "required",
        "var", "when", "where", "with", "yield",
    };

    public Language Language => Language.CSharp;

    public string NothingToAnalyze => ICSharpSources.NothingToAnalyze;

    public (IReadOnlyList<NormalizedSource> Files, string? Failure) Read(IRepository repository)
    {
        var (files, failure) = sources.Read(repository);
        return (
            files.Select(file => new NormalizedSource(
                file.Path,
                LineTokenizer.Read(file.Source.Masked, file.Source.MaskedRegions, Keywords))).ToList(),
            failure);
    }
}
