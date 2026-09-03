namespace Harness.Infrastructure.Languages.TypeScript;

/// <summary>
/// Counts the physical lines of a TypeScript or JavaScript file that carry a comment. The
/// walk skips string, template and regular expression literals so comment-shaped text inside
/// them is not counted, and descends into template holes because they hold code.
/// </summary>
internal static class TypeScriptCommentLines
{
    private static readonly HashSet<string> RegexPrecedingKeywords = new(StringComparer.Ordinal)
    {
        "return", "typeof", "instanceof", "in", "of", "case", "do", "else", "new", "delete",
        "void", "throw", "yield", "await",
    };

    public static (int CommentLines, int AuthoredLines) Count(string text)
    {
        var scanner = new Scanner(text);
        scanner.ScanCode(0, untilBrace: false);
        return (scanner.CommentLines, AuthoredLines(text));
    }

    private static int AuthoredLines(string text)
        => text.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));

    private sealed class Scanner(string text)
    {
        private readonly HashSet<int> commentLines = [];
        private int line;
        private char last;
        private string lastWord = string.Empty;

        public int CommentLines => commentLines.Count;

        public int ScanCode(int index, bool untilBrace)
        {
            var depth = 0;
            while (index < text.Length)
            {
                var character = text[index];
                if (character == '\n')
                {
                    line++;
                    index++;
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    index++;
                    continue;
                }

                if (character == '/' && index + 1 < text.Length && text[index + 1] is '/' or '*')
                {
                    index = SkipComment(index);
                    continue;
                }

                if (character is '\'' or '"')
                {
                    index = SkipQuoted(index);
                    continue;
                }

                if (character == '`')
                {
                    index = SkipTemplate(index);
                    continue;
                }

                if (character == '/' && RegexAllowed() && RegexEnd(index) is var end && end > index)
                {
                    index = end;
                    Seen('/');
                    continue;
                }

                if (char.IsLetter(character) || character is '_' or '$')
                {
                    var stop = index;
                    while (stop < text.Length && (char.IsLetterOrDigit(text[stop]) || text[stop] is '_' or '$'))
                    {
                        stop++;
                    }

                    lastWord = text[index..stop];
                    last = text[stop - 1];
                    index = stop;
                    continue;
                }

                if (character == '}' && untilBrace && depth == 0)
                {
                    return index + 1;
                }

                depth += character == '{' ? 1 : character == '}' ? -1 : 0;
                Seen(character);
                index++;
            }

            return text.Length;
        }

        private void Seen(char character)
        {
            last = character;
            lastWord = string.Empty;
        }

        private bool RegexAllowed()
            => last == '\0'
                || "(,=:[!&|?{};+-*%<>~^".Contains(last)
                || RegexPrecedingKeywords.Contains(lastWord);

        private int SkipComment(int index)
        {
            var end = text[index + 1] == '/' ? EndOfLine(index) : BlockCommentEnd(index);
            for (var scan = index; scan < end; scan++)
            {
                commentLines.Add(line);
                if (text[scan] == '\n')
                {
                    line++;
                }
            }

            return end;
        }

        private int SkipQuoted(int index)
        {
            var quote = text[index];
            var scan = index + 1;
            while (scan < text.Length)
            {
                var character = text[scan];
                if (character == '\\')
                {
                    scan += 2;
                    continue;
                }

                // An unterminated regular literal ends at the physical line.
                if (character == '\n')
                {
                    break;
                }

                scan++;
                if (character == quote)
                {
                    break;
                }
            }

            Seen(quote);
            return Math.Min(scan, text.Length);
        }

        private int SkipTemplate(int index)
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

                if (character == '`')
                {
                    scan++;
                    break;
                }

                if (character == '$' && scan + 1 < text.Length && text[scan + 1] == '{')
                {
                    scan = ScanCode(scan + 2, untilBrace: true);
                    continue;
                }

                if (character == '\n')
                {
                    line++;
                }

                scan++;
            }

            Seen('`');
            return Math.Min(scan, text.Length);
        }

        // Returns the start index when no literal closes on this line, so the slash is
        // read as division instead.
        private int RegexEnd(int index)
        {
            var scan = index + 1;
            var inClass = false;
            while (scan < text.Length)
            {
                var character = text[scan];
                if (character == '\n')
                {
                    return index;
                }

                if (character == '\\')
                {
                    scan += 2;
                    continue;
                }

                if (character == '[')
                {
                    inClass = true;
                }
                else if (character == ']')
                {
                    inClass = false;
                }
                else if (character == '/' && !inClass)
                {
                    scan++;
                    while (scan < text.Length && char.IsLetter(text[scan]))
                    {
                        scan++;
                    }

                    return scan;
                }

                scan++;
            }

            return index;
        }

        private int EndOfLine(int index)
        {
            var end = text.IndexOf('\n', index);
            return end < 0 ? text.Length : end;
        }

        private int BlockCommentEnd(int index)
        {
            var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
            return end < 0 ? text.Length : end + 2;
        }
    }
}
