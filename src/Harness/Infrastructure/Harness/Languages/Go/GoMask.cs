using Harness.Languages;

namespace Harness.Infrastructure.Languages.Go;

/// <summary>
/// Reduces Go to code by replacing comment text and literal content with spaces. Newlines and
/// offsets survive, so every measurement taken on the result still points at the original
/// file. Go has three literal forms — interpreted strings, raw strings across lines, runes —
/// and two comment forms; there is no preprocessor.
/// </summary>
internal static class GoMask
{
    public static (string Masked, List<MaskedRegion> Regions) Apply(string text)
    {
        var buffer = text.ToCharArray();
        var regions = new List<MaskedRegion>();
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '/' && index + 1 < text.Length && text[index + 1] is '/' or '*')
            {
                var end = text[index + 1] == '/' ? EndOfLine(text, index) : BlockCommentEnd(text, index);
                index = Record(regions, buffer, text, index, end, MaskedContent.Comment);
                continue;
            }

            index = character switch
            {
                '"' => Record(regions, buffer, text, index, QuotedEnd(text, index, '"', escapes: true), MaskedContent.StringLiteral),
                '`' => Record(regions, buffer, text, index, RawEnd(text, index), MaskedContent.StringLiteral),
                '\'' => Record(regions, buffer, text, index, QuotedEnd(text, index, '\'', escapes: true), MaskedContent.CharacterLiteral),
                _ => index + 1,
            };
        }

        return (new string(buffer), regions);
    }

    private static int Record(
        List<MaskedRegion> regions,
        char[] buffer,
        string text,
        int from,
        int to,
        MaskedContent content)
    {
        var stop = Math.Min(to, text.Length);
        for (var index = from; index < stop; index++)
        {
            if (text[index] != '\n')
            {
                buffer[index] = ' ';
            }
        }

        if (stop > from)
        {
            regions.Add(new MaskedRegion(from, stop, content));
        }

        return stop;
    }

    private static int EndOfLine(string text, int index)
    {
        var end = text.IndexOf('\n', index);
        return end < 0 ? text.Length : end;
    }

    private static int BlockCommentEnd(string text, int index)
    {
        var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + 2;
    }

    // A raw string runs to the next backtick across lines and knows no escapes.
    private static int RawEnd(string text, int index)
    {
        var end = text.IndexOf('`', index + 1);
        return end < 0 ? text.Length : end + 1;
    }

    // Interpreted strings and runes end at their quote or at the physical line, as the
    // compiler reads an unterminated one.
    private static int QuotedEnd(string text, int index, char quote, bool escapes)
    {
        var scan = index + 1;
        while (scan < text.Length)
        {
            var character = text[scan];
            if (escapes && character == '\\')
            {
                scan += 2;
                continue;
            }

            if (character == '\n')
            {
                return scan;
            }

            scan++;
            if (character == quote)
            {
                break;
            }
        }

        return Math.Min(scan, text.Length);
    }
}
