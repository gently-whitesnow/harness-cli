using System.Text;
using Harness.Checks.CSharp;

namespace Harness.Checks.Duplication;

/// <param name="Line">The 1-based physical line the tokens start on.</param>
/// <param name="Tokens">The normalized token sequence, space separated.</param>
/// <param name="TokenCount">How many tokens the line contributed, which is its density.</param>
internal sealed record NormalizedLine(int Line, string Tokens, int TokenCount);

/// <summary>
/// Reduces C# to the unit duplication compares: one normalized token sequence per physical
/// line that carries tokens. Everything a rename or a reformat can change is removed, and
/// nothing else is. The exact rules are the whole claim of the comparison, so they are
/// short enough to state:
/// <list type="bullet">
///   <item>a comment or a preprocessor directive contributes nothing;</item>
///   <item>a string literal is one <c>"</c> token and a character literal one <c>'</c>,
///     whatever they contain;</item>
///   <item>a numeric literal is one <c>#</c> token;</item>
///   <item>a word is kept when it is a C# keyword and becomes <c>n</c> otherwise, so every
///     identifier — type, member, parameter, local — reads the same;</item>
///   <item>every other character is its own token;</item>
///   <item>whitespace separates tokens and never survives, and a line with no tokens leaves
///     no trace at all.</item>
/// </list>
/// </summary>
internal sealed class CSharpNormalizer
{
    /// <summary>
    /// The words kept as themselves. Reserved words plus the contextual words that carry
    /// structure a reader would notice; every other word is an identifier as far as this
    /// comparison is concerned.
    /// </summary>
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

    private readonly CSharpSource source;
    private readonly List<NormalizedLine> lines = [];
    private readonly StringBuilder tokens = new();
    private int tokenCount;
    private int currentLine;

    private CSharpNormalizer(CSharpSource source) => this.source = source;

    public static IReadOnlyList<NormalizedLine> Read(CSharpSource source)
    {
        var normalizer = new CSharpNormalizer(source);
        normalizer.Walk();
        return normalizer.lines;
    }

    private void Walk()
    {
        var text = source.Masked;
        var regions = source.MaskedRegions;
        var region = 0;

        var index = 0;
        while (index < text.Length)
        {
            while (region < regions.Count && regions[region].End <= index)
            {
                region++;
            }

            if (region < regions.Count && regions[region].Start == index)
            {
                index = Take(regions[region]);
                region++;
                continue;
            }

            var character = text[index];
            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (char.IsLetter(character) || character is '_' or '@')
            {
                var start = index;
                index = EndOfWord(text, index);
                var word = text[start..index];
                Emit(start, Keywords.Contains(word) ? word : "n");
                continue;
            }

            if (char.IsDigit(character))
            {
                var start = index;
                index = EndOfWord(text, index);
                Emit(start, "#");
                continue;
            }

            Emit(index, character.ToString());
            index++;
        }

        Flush();
    }

    /// <summary>
    /// One masked region as the single token its content is worth. A literal is evidence
    /// that something was passed; which literal it was is exactly what normalization drops.
    /// </summary>
    /// <returns>The offset the walk continues from.</returns>
    private int Take(MaskedRegion region)
    {
        switch (region.Content)
        {
            case MaskedContent.StringLiteral:
                Emit(region.Start, "\"");
                break;

            case MaskedContent.CharacterLiteral:
                Emit(region.Start, "'");
                break;

            default:
                break;
        }

        return Math.Max(region.End, region.Start + 1);
    }

    private void Emit(int offset, string token)
    {
        var line = source.LineOf(offset);
        if (line != currentLine)
        {
            Flush();
            currentLine = line;
        }

        if (tokens.Length > 0)
        {
            tokens.Append(' ');
        }

        tokens.Append(token);
        tokenCount++;
    }

    private void Flush()
    {
        if (tokenCount > 0)
        {
            lines.Add(new NormalizedLine(currentLine, tokens.ToString(), tokenCount));
        }

        tokens.Clear();
        tokenCount = 0;
    }

    /// <summary>
    /// The end of a word or a number. Both are read the same way, so `1_000`, `0xFF` and
    /// `1.5f` end where a reader expects and never leave a fragment behind as its own token.
    /// </summary>
    private static int EndOfWord(string text, int index)
    {
        while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '@'))
        {
            index++;
        }

        return index;
    }
}
