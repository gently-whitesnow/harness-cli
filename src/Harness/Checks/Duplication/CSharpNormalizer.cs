using System.Text;
using Harness.Checks.CSharp;

namespace Harness.Checks.Duplication;

/// <summary>
/// Normalizes each physical C# line for lexical duplication: comments and directives vanish;
/// literals and numbers become type-shaped tokens; keywords survive; identifiers become `n`;
/// punctuation survives; whitespace and empty normalized lines vanish.
/// </summary>
internal sealed class CSharpNormalizer
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

    private static int EndOfWord(string text, int index)
    {
        while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '@'))
        {
            index++;
        }

        return index;
    }
}
