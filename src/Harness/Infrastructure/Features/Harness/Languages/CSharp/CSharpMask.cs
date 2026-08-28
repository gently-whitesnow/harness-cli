using Harness.Languages.CSharp;

namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>
/// Reduces C# to code by replacing comment text, literal content and preprocessor directives
/// with spaces. Newlines and offsets survive, so every measurement taken on the result still
/// points at the original file.
/// </summary>
internal static class CSharpMask
{
    public static (string Masked, List<MaskedRegion> Regions) Apply(string text)
    {
        var buffer = text.ToCharArray();
        var regions = new List<MaskedRegion>();

        var index = 0;
        var atLineStart = true;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '\n')
            {
                atLineStart = true;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            // A preprocessor directive is not code the metrics measure, and `#if` must not
            // be read as a branch: the compiler decides conditional compilation, not this.
            if (atLineStart && character == '#')
            {
                index = Record(regions, buffer, text, index, EndOfLine(text, index), MaskedContent.Preprocessor);
                continue;
            }

            atLineStart = false;
            index = Skip(regions, buffer, text, index);
        }

        return (new string(buffer), regions);
    }

    private static int Skip(List<MaskedRegion> regions, char[] buffer, string text, int index)
    {
        var character = text[index];
        if (character == '/' && index + 1 < text.Length && text[index + 1] is '/' or '*')
        {
            var end = text[index + 1] == '/' ? EndOfLine(text, index) : BlockCommentEnd(text, index);
            return Record(regions, buffer, text, index, end, MaskedContent.Comment);
        }

        if (character == '\'')
        {
            return Record(
                regions, buffer, text, index, CharacterLiteralEnd(text, index), MaskedContent.CharacterLiteral);
        }

        if (character is '"' or '@' or '$')
        {
            var end = StringLiteralEnd(text, index);
            if (end > index)
            {
                return Record(regions, buffer, text, index, end, MaskedContent.StringLiteral);
            }
        }

        return index + 1;
    }

    private static int Record(
        List<MaskedRegion> regions,
        char[] buffer,
        string text,
        int from,
        int to,
        MaskedContent content)
    {
        var stop = Blank(buffer, text, from, to);
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

    private static int CharacterLiteralEnd(string text, int index)
    {
        var scan = index + 1;
        while (scan < text.Length)
        {
            var character = text[scan];
            if (character == '\\')
            {
                scan += 2;
                continue;
            }

            if (character == '\n')
            {
                break;
            }

            scan++;
            if (character == '\'')
            {
                break;
            }
        }

        return Math.Min(scan, text.Length);
    }

    private static int StringLiteralEnd(string text, int start)
    {
        var scan = start;
        var dollars = 0;
        var verbatim = false;
        while (scan < text.Length && text[scan] is '@' or '$')
        {
            if (text[scan] == '$')
            {
                dollars++;
            }
            else
            {
                verbatim = true;
            }

            scan++;
        }

        if (scan >= text.Length || text[scan] != '"')
        {
            return start;
        }

        var quotes = RunLength(text, scan, '"');
        return quotes >= 3 && !verbatim
            ? RawEnd(text, scan + quotes, quotes, dollars)
            : QuotedEnd(text, scan + 1, verbatim, dollars);
    }

    private static int RawEnd(string text, int index, int quotes, int dollars)
    {
        while (index < text.Length)
        {
            if (dollars > 0 && text[index] == '{')
            {
                index = HoleEnd(text, index, dollars);
                continue;
            }

            if (text[index] != '"')
            {
                index++;
                continue;
            }

            var run = RunLength(text, index, '"');
            if (run >= quotes)
            {
                return index + run;
            }

            index += run;
        }

        return text.Length;
    }

    private static int QuotedEnd(string text, int index, bool verbatim, int dollars)
    {
        while (index < text.Length)
        {
            var character = text[index];
            if (dollars > 0 && character == '{')
            {
                index = HoleEnd(text, index, dollars);
                continue;
            }

            if (character == '"')
            {
                // Inside a verbatim literal a doubled quote is an escaped one.
                if (!verbatim || RunLength(text, index, '"') == 1)
                {
                    return index + 1;
                }

                index += 2;
                continue;
            }

            if (!verbatim && character == '\\')
            {
                index += 2;
                continue;
            }

            // Unterminated regular literals end at the physical line.
            if (!verbatim && character == '\n')
            {
                return index;
            }

            index++;
        }

        return text.Length;
    }

    // Interpolation holes are walked because their quotes, braces and nested literals can
    // otherwise hide the end of the containing literal and everything after it. N dollar
    // signs require N braces to open a hole; with one, only an odd brace run opens one,
    // because doubled braces escape each other.
    private static int HoleEnd(string text, int index, int dollars)
    {
        var run = RunLength(text, index, '{');
        index += run;

        if (!(dollars == 1 ? run % 2 == 1 : run >= dollars))
        {
            return index;
        }

        var depth = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '}' && depth == 0)
            {
                return index + Math.Min(RunLength(text, index, '}'), dollars);
            }

            if (character is '{' or '}')
            {
                depth += character == '{' ? 1 : -1;
                index++;
                continue;
            }

            index = InsideHole(text, index);
        }

        return text.Length;
    }

    private static int InsideHole(string text, int index)
    {
        var character = text[index];
        if (character == '\'')
        {
            return CharacterLiteralEnd(text, index);
        }

        if (character is '"' or '@' or '$' && StringLiteralEnd(text, index) is var end && end > index)
        {
            return end;
        }

        if (character == '/' && index + 1 < text.Length && text[index + 1] is '/' or '*')
        {
            return text[index + 1] == '/' ? EndOfLine(text, index) : BlockCommentEnd(text, index);
        }

        return index + 1;
    }

    private static int RunLength(string text, int index, char character)
    {
        var run = 0;
        while (index + run < text.Length && text[index + run] == character)
        {
            run++;
        }

        return run;
    }

    private static int Blank(char[] buffer, string text, int from, int to)
    {
        var stop = Math.Min(to, text.Length);
        for (var index = from; index < stop; index++)
        {
            if (text[index] != '\n')
            {
                buffer[index] = ' ';
            }
        }

        return stop;
    }
}
