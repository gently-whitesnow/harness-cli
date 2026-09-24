using Harness.Languages;

namespace Harness.Infrastructure.Languages.TypeScript;

/// <summary>Preserves offsets while masking comments, literals and regular expressions.</summary>
internal static class TypeScriptMask
{
    public static (string Masked, List<MaskedRegion> Regions) Apply(string text)
    {
        var scanner = new Scanner(text);
        scanner.ScanCode(0, false);
        return (new string(scanner.Chars), scanner.Regions);
    }

    private sealed class Scanner(string text)
    {
        public char[] Chars { get; } = text.ToCharArray();
        public List<MaskedRegion> Regions { get; } = [];

        private char last;
        private string lastWord = string.Empty;

        public int ScanCode(int index, bool untilBrace)
        {
            var depth = 0;
            while (index < text.Length)
            {
                var c = text[index];
                if (char.IsWhiteSpace(c))
                {
                    index++;
                    continue;
                }

                if (c == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    var end = text.IndexOf('\n', index);
                    index = Record(index, end < 0 ? text.Length : end, MaskedContent.Comment);
                    continue;
                }

                if (c == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    index = Record(index, end < 0 ? text.Length : end + 2, MaskedContent.Comment);
                    continue;
                }

                if (c is '\'' or '"')
                {
                    index = Record(index, QuotedEnd(index, c), MaskedContent.StringLiteral);
                    Seen(c);
                    continue;
                }

                if (c == '`')
                {
                    index = ScanTemplate(index);
                    Seen('`');
                    continue;
                }

                if (c == '/' && RegexAllowed() && RegexEnd(index) is var regexEnd && regexEnd > index)
                {
                    index = Record(index, regexEnd, MaskedContent.StringLiteral);
                    Seen('/');
                    continue;
                }

                if (char.IsLetter(c) || c is '_' or '$')
                {
                    var end = index + 1;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '$'))
                    {
                        end++;
                    }

                    lastWord = text[index..end];
                    last = text[end - 1];
                    index = end;
                    continue;
                }

                if (c == '}' && untilBrace && depth == 0)
                {
                    return index + 1;
                }

                depth += c == '{' ? 1 : c == '}' ? -1 : 0;
                Seen(c);
                index++;
            }

            return text.Length;
        }

        private int ScanTemplate(int start)
        {
            var segment = start;
            var index = start + 1;
            while (index < text.Length)
            {
                if (text[index] == '\\')
                {
                    index = Math.Min(index + 2, text.Length);
                    continue;
                }

                if (text[index] == '`')
                {
                    return Record(segment, index + 1, MaskedContent.StringLiteral);
                }

                if (text[index] == '$' && index + 1 < text.Length && text[index + 1] == '{')
                {
                    Record(segment, index + 2, MaskedContent.StringLiteral);
                    index = ScanCode(index + 2, true);
                    segment = index;
                    continue;
                }

                index++;
            }

            return Record(segment, text.Length, MaskedContent.StringLiteral);
        }

        private int QuotedEnd(int start, char quote)
        {
            var index = start + 1;
            while (index < text.Length)
            {
                if (text[index] == '\\')
                {
                    index = Math.Min(index + 2, text.Length);
                    continue;
                }

                if (text[index] == '\n')
                {
                    break;
                }

                if (text[index++] == quote)
                {
                    break;
                }
            }

            return index;
        }

        private bool RegexAllowed()
            => last == '\0' || "(,=:[!&|?{};+-*%<>~^".Contains(last)
                || lastWord is "return" or "typeof" or "instanceof" or "in" or "of" or "case" or "do" or "else" or "throw";

        private int RegexEnd(int start)
        {
            var index = start + 1;
            var characterClass = false;
            while (index < text.Length && text[index] != '\n')
            {
                var c = text[index];
                if (c == '\\')
                {
                    index = Math.Min(index + 2, text.Length);
                    continue;
                }

                if (c == '[')
                {
                    characterClass = true;
                }
                else if (c == ']')
                {
                    characterClass = false;
                }
                else if (c == '/' && !characterClass)
                {
                    index++;
                    while (index < text.Length && char.IsLetter(text[index]))
                    {
                        index++;
                    }

                    return index;
                }

                index++;
            }

            return start;
        }

        private int Record(int start, int end, MaskedContent content)
        {
            if (end <= start)
            {
                return end;
            }

            Regions.Add(new MaskedRegion(start, end, content));
            for (var index = start; index < end; index++)
            {
                if (Chars[index] != '\n')
                {
                    Chars[index] = ' ';
                }
            }

            return end;
        }

        private void Seen(char c)
        {
            last = c;
            lastWord = string.Empty;
        }
    }
}
