using System.Text;

namespace Harness.Languages.Duplication;

/// <summary>
/// Turns masked source into one token sequence per physical line, by rules that no language
/// changes: masked comments and directives vanish; a string literal is one `"` and a character
/// literal one `'`, whatever they contained; a number is `#`; a word the language reserves
/// survives and any other word is `n`; every other character is itself; whitespace only
/// separates. A language supplies its mask and its reserved words and nothing else.
/// </summary>
internal sealed class LineTokenizer
{
    private readonly string text;
    private readonly IReadOnlyList<MaskedRegion> regions;
    private readonly IReadOnlySet<string> keywords;
    private readonly List<NormalizedLine> lines = [];
    private readonly StringBuilder tokens = new();
    private int tokenCount;
    private int currentLine = 1;
    private int emittedLine;

    private LineTokenizer(string text, IReadOnlyList<MaskedRegion> regions, IReadOnlySet<string> keywords)
    {
        this.text = text;
        this.regions = regions;
        this.keywords = keywords;
    }

    public static IReadOnlyList<NormalizedLine> Read(
        string masked,
        IReadOnlyList<MaskedRegion> regions,
        IReadOnlySet<string> keywords)
    {
        var tokenizer = new LineTokenizer(masked, regions, keywords);
        tokenizer.Walk();
        return tokenizer.lines;
    }

    private void Walk()
    {
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
            if (character == '\n')
            {
                currentLine++;
                index++;
                continue;
            }

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
                Emit(keywords.Contains(word) ? word : "n");
                continue;
            }

            if (char.IsDigit(character))
            {
                index = EndOfWord(text, index);
                Emit("#");
                continue;
            }

            Emit(character.ToString());
            index++;
        }

        Flush();
    }

    private int Take(MaskedRegion region)
    {
        switch (region.Content)
        {
            case MaskedContent.StringLiteral:
                Emit("\"");
                break;

            case MaskedContent.CharacterLiteral:
                Emit("'");
                break;

            default:
                break;
        }

        var end = Math.Min(text.Length, Math.Max(region.End, region.Start + 1));
        for (var index = region.Start; index < end; index++)
        {
            if (text[index] == '\n')
            {
                currentLine++;
            }
        }

        return end;
    }

    private void Emit(string token)
    {
        if (currentLine != emittedLine)
        {
            Flush();
            emittedLine = currentLine;
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
            lines.Add(new NormalizedLine(emittedLine, tokens.ToString(), tokenCount));
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
