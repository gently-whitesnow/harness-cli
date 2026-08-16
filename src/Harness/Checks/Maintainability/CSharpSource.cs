namespace Harness.Checks.Maintainability;

/// <summary>
/// One C# file reduced to what a lexical reader can state with certainty: the same text
/// with comment, string, character-literal and preprocessor content replaced by spaces,
/// plus which physical lines carry code. Every measurement reads this instead of the raw
/// text, so a keyword inside a comment and a brace inside a string cannot be mistaken for
/// control flow or for structure.
/// </summary>
/// <remarks>
/// Masking preserves every offset and every newline, so a position in the masked text is
/// the same position in the file the reader will open.
/// </remarks>
internal sealed class CSharpSource
{
    private readonly int[] lineStarts;
    private readonly bool[] isLogical;

    private CSharpSource(string path, string masked, int[] lineStarts, bool[] isLogical)
    {
        Path = path;
        Masked = masked;
        this.lineStarts = lineStarts;
        this.isLogical = isLogical;
    }

    /// <summary>Repository-relative path, as findings report it.</summary>
    public string Path { get; }

    /// <summary>The file with everything that is not code blanked out.</summary>
    public string Masked { get; }

    public int LineCount => lineStarts.Length;

    /// <summary>Physical lines that carry code or continue a multi-line string literal.</summary>
    public int LogicalLines => LogicalLinesBetween(1, LineCount);

    public static CSharpSource Read(string path, string text)
    {
        var (masked, literals) = Mask(text);
        var lineStarts = LineStarts(masked);
        return new CSharpSource(path, masked, lineStarts, ClassifyLines(masked, lineStarts, literals));
    }

    /// <summary>The 1-based physical line an offset in the masked text falls on.</summary>
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

    /// <summary>The masked text of a line range, which is what token counting reads.</summary>
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

    /// <summary>
    /// A line is logical when code survives masking on it, or when it is the continuation
    /// of a multi-line string literal — the literal is content the file carries, even
    /// though none of its characters are code.
    /// </summary>
    private static bool[] ClassifyLines(string masked, int[] lineStarts, List<(int Start, int End)> literals)
    {
        var isLogical = new bool[lineStarts.Length];
        for (var line = 0; line < lineStarts.Length; line++)
        {
            var start = lineStarts[line];
            var end = line + 1 < lineStarts.Length ? lineStarts[line + 1] : masked.Length;
            isLogical[line] = masked.AsSpan(start, end - start).ContainsAnyExcept(" \t\r\n");
        }

        foreach (var (start, end) in literals)
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

    private static int LineIndexOf(int[] lineStarts, int index)
    {
        var found = Array.BinarySearch(lineStarts, index);
        return found >= 0 ? found : ~found - 1;
    }

    /// <summary>
    /// Blanks every region a C# lexer would not hand to the parser. Returns the masked text
    /// together with the spans of the string literals, which the caller needs to tell a
    /// blank line apart from the inside of a multi-line literal.
    /// </summary>
    private static (string Masked, List<(int Start, int End)> Literals) Mask(string text)
    {
        var buffer = text.ToCharArray();
        var literals = new List<(int Start, int End)>();

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
                index = BlankToEndOfLine(buffer, text, index);
                continue;
            }

            atLineStart = false;

            if (character == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                {
                    index = BlankToEndOfLine(buffer, text, index);
                    continue;
                }

                if (text[index + 1] == '*')
                {
                    index = BlankBlockComment(buffer, text, index);
                    continue;
                }
            }

            if (character == '\'')
            {
                index = Blank(buffer, text, index, CharacterLiteralEnd(text, index));
                continue;
            }

            if (character is '"' or '@' or '$')
            {
                var end = StringLiteralEnd(text, index);
                if (end > index)
                {
                    literals.Add((index, end));
                    index = Blank(buffer, text, index, end);
                    continue;
                }
            }

            index++;
        }

        return (new string(buffer), literals);
    }

    private static int BlankToEndOfLine(char[] buffer, string text, int index)
        => Blank(buffer, text, index, EndOfLine(text, index));

    private static int BlankBlockComment(char[] buffer, string text, int index)
        => Blank(buffer, text, index, BlockCommentEnd(text, index));

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

    /// <summary>
    /// The end of a string literal in any of the forms C# accepts: regular, verbatim, raw,
    /// and the interpolated variants of each. Interpolation holes are walked rather than
    /// skipped over, because a hole may contain quotes, braces and comments of its own; a
    /// reader that ignored that would lose the end of the literal and, with it, the
    /// structure of everything after it.
    /// </summary>
    /// <returns>The offset after the literal, or <paramref name="start"/> when none begins here.</returns>
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

                // A doubled quote is an escaped quote in a verbatim literal.
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

                // An unterminated regular literal ends at the line, as the compiler says.
                if (character == '\n')
                {
                    return index;
                }
            }

            index++;
        }

        return text.Length;
    }

    /// <summary>
    /// Walks one interpolation hole, or one escaped brace run. In a literal with N dollar
    /// signs, N consecutive braces open a hole; with one dollar sign a doubled brace is an
    /// escape instead, so only an odd run opens one.
    /// </summary>
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

    /// <summary>Newlines survive, so masked offsets and line numbers stay the file's own.</summary>
    /// <returns>The offset the caller continues from.</returns>
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
