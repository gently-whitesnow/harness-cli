namespace Harness.Checks.CSharp;

internal enum MaskedContent
{
    Comment,
    Preprocessor,
    StringLiteral,
    CharacterLiteral,
}

internal sealed record MaskedRegion(int Start, int End, MaskedContent Content);

/// <summary>
/// C# reduced to code by replacing comments, literals and directives with spaces.
/// Newlines and offsets survive, so findings still point to the original file.
/// </summary>
internal sealed class CSharpSource
{
    private readonly int[] lineStarts;
    private readonly bool[] isLogical;
    private readonly bool[] hasComment;

    private CSharpSource(
        string path,
        string masked,
        IReadOnlyList<MaskedRegion> maskedRegions,
        int[] lineStarts,
        bool[] isLogical,
        bool[] hasComment)
    {
        Path = path;
        Masked = masked;
        MaskedRegions = maskedRegions;
        this.lineStarts = lineStarts;
        this.isLogical = isLogical;
        this.hasComment = hasComment;
    }

    public string Path { get; }

    public string Masked { get; }

    public IReadOnlyList<MaskedRegion> MaskedRegions { get; }

    public int LineCount => lineStarts.Length;

    public int LogicalLines => LogicalLinesBetween(1, LineCount);

    public int CommentLines => hasComment.Count(line => line);

    public int AuthoredLines
        => Enumerable.Range(0, LineCount).Count(line => isLogical[line] || hasComment[line]);

    public static CSharpSource Read(string path, string text)
    {
        var (masked, regions) = Mask(text);
        var lineStarts = LineStarts(masked);
        return new CSharpSource(
            path,
            masked,
            regions,
            lineStarts,
            ClassifyLines(masked, lineStarts, regions),
            ClassifyCommentLines(lineStarts, regions));
    }

    public int LineOf(int index)
    {
        var found = Array.BinarySearch(lineStarts, index);
        return found >= 0 ? found + 1 : ~found;
    }

    public int LogicalLinesBetween(int firstLine, int lastLine)
    {
        var first = Math.Max(1, firstLine);
        var last = Math.Min(LineCount, lastLine);

        var count = 0;
        for (var line = first; line <= last; line++)
        {
            if (isLogical[line - 1])
            {
                count++;
            }
        }

        return count;
    }

    public ReadOnlySpan<char> TextBetween(int firstLine, int lastLine)
    {
        if (firstLine > LineCount || lastLine < firstLine)
        {
            return ReadOnlySpan<char>.Empty;
        }

        var start = lineStarts[Math.Max(1, firstLine) - 1];
        var last = Math.Min(LineCount, lastLine);
        var end = last < LineCount ? lineStarts[last] : Masked.Length;
        return Masked.AsSpan(start, end - start);
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n' && index + 1 < text.Length)
            {
                starts.Add(index + 1);
            }
        }

        return [.. starts];
    }

    // Multi-line literal continuations carry file content even though masking removes it.
    private static bool[] ClassifyLines(string masked, int[] lineStarts, List<MaskedRegion> regions)
    {
        var isLogical = new bool[lineStarts.Length];
        for (var line = 0; line < lineStarts.Length; line++)
        {
            var start = lineStarts[line];
            var end = line + 1 < lineStarts.Length ? lineStarts[line + 1] : masked.Length;
            isLogical[line] = masked.AsSpan(start, end - start).ContainsAnyExcept(" \t\r\n");
        }

        foreach (var (start, end, _) in regions.Where(region => region.Content == MaskedContent.StringLiteral))
        {
            for (var index = start; index < end; index++)
            {
                if (masked[index] == '\n' && index + 1 < masked.Length)
                {
                    var line = LineIndexOf(lineStarts, index + 1);
                    isLogical[line] = true;
                }
            }
        }

        return isLogical;
    }

    private static bool[] ClassifyCommentLines(int[] lineStarts, List<MaskedRegion> regions)
    {
        var hasComment = new bool[lineStarts.Length];
        foreach (var region in regions.Where(region => region.Content == MaskedContent.Comment))
        {
            var first = LineIndexOf(lineStarts, region.Start);
            var last = LineIndexOf(lineStarts, Math.Max(region.Start, region.End - 1));
            for (var line = first; line <= last; line++)
            {
                hasComment[line] = true;
            }
        }

        return hasComment;
    }

    private static int LineIndexOf(int[] lineStarts, int index)
    {
        var found = Array.BinarySearch(lineStarts, index);
        return found >= 0 ? found : ~found - 1;
    }

    private static (string Masked, List<MaskedRegion> Regions) Mask(string text)
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

            if (character == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                {
                    index = Record(regions, buffer, text, index, EndOfLine(text, index), MaskedContent.Comment);
                    continue;
                }

                if (text[index + 1] == '*')
                {
                    index = Record(
                        regions, buffer, text, index, BlockCommentEnd(text, index), MaskedContent.Comment);
                    continue;
                }
            }

            if (character == '\'')
            {
                index = Record(
                    regions,
                    buffer,
                    text,
                    index,
                    CharacterLiteralEnd(text, index),
                    MaskedContent.CharacterLiteral);
                continue;
            }

            if (character is '"' or '@' or '$')
            {
                var end = StringLiteralEnd(text, index);
                if (end > index)
                {
                    index = Record(regions, buffer, text, index, end, MaskedContent.StringLiteral);
                    continue;
                }
            }

            index++;
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

    // Interpolation holes are walked because their quotes, braces and nested literals can
    // otherwise hide the end of the containing literal and everything after it.
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
        var raw = quotes >= 3 && !verbatim;
        var index = raw ? scan + quotes : scan + 1;

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
                var run = RunLength(text, index, '"');
                if (raw)
                {
                    if (run >= quotes)
                    {
                        return index + run;
                    }

                    index += run;
                    continue;
                }

                if (verbatim && run > 1)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            if (!raw && !verbatim)
            {
                if (character == '\\')
                {
                    index += 2;
                    continue;
                }

                // Unterminated regular literals end at the physical line.
                if (character == '\n')
                {
                    return index;
                }
            }

            index++;
        }

        return text.Length;
    }

    // N dollar signs require N braces to open a hole; with one dollar sign, only an odd
    // brace run opens one because doubled braces escape each other.
    private static int HoleEnd(string text, int index, int dollars)
    {
        var run = RunLength(text, index, '{');
        index += run;

        var opensHole = dollars == 1 ? run % 2 == 1 : run >= dollars;
        if (!opensHole)
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

            if (character == '\'')
            {
                index = CharacterLiteralEnd(text, index);
                continue;
            }

            if (character is '"' or '@' or '$')
            {
                var end = StringLiteralEnd(text, index);
                if (end > index)
                {
                    index = end;
                    continue;
                }
            }

            if (character == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                {
                    index = EndOfLine(text, index);
                    continue;
                }

                if (text[index + 1] == '*')
                {
                    index = BlockCommentEnd(text, index);
                    continue;
                }
            }

            index++;
        }

        return text.Length;
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
