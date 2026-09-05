namespace Harness.Infrastructure.Languages.Yaml;

/// <summary>
/// Counts the physical lines of a YAML document that carry a comment. YAML has one comment
/// form, `#` at the start of a line or after whitespace, and two places where that same
/// character is content: a quoted scalar and the body of a block scalar.
/// </summary>
internal static class YamlCommentLines
{
    public static (int CommentLines, int AuthoredLines) Count(string text)
    {
        var comments = 0;
        var authored = 0;
        var blockIndent = -1;
        var quote = '\0';
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            authored++;
            var indent = line.Length - line.TrimStart(' ').Length;
            if (blockIndent >= 0 && indent > blockIndent)
            {
                continue;
            }

            blockIndent = -1;
            var (commentAt, opensBlock) = Scan(line, ref quote);
            if (commentAt >= 0)
            {
                comments++;
            }

            if (opensBlock)
            {
                blockIndent = indent;
            }
        }

        return (comments, authored);
    }

    private static (int CommentAt, bool OpensBlock) Scan(string line, ref char quote)
    {
        var commentAt = -1;
        for (var index = 0; index < line.Length && commentAt < 0; index++)
        {
            var character = line[index];
            if (quote != '\0')
            {
                index = InsideQuote(line, index, ref quote);
                continue;
            }

            var afterBreak = index == 0 || char.IsWhiteSpace(line[index - 1]) || line[index - 1] is '[' or '{' or ',';
            if (character == '#' && afterBreak)
            {
                commentAt = index;
            }
            else if (character is '\'' or '"' && afterBreak)
            {
                quote = character;
            }
        }

        var content = (commentAt < 0 ? line : line[..commentAt]).TrimEnd();
        return (commentAt, quote == '\0' && OpensBlockScalar(content));
    }

    private static int InsideQuote(string line, int index, ref char quote)
    {
        var character = line[index];
        if (quote == '"' && character == '\\')
        {
            return index + 1;
        }

        if (character != quote)
        {
            return index;
        }

        // A doubled single quote is an escaped one, not the end of the scalar.
        if (quote == '\'' && index + 1 < line.Length && line[index + 1] == '\'')
        {
            return index + 1;
        }

        quote = '\0';
        return index;
    }

    private static bool OpensBlockScalar(string content)
    {
        var separator = content.LastIndexOf(' ');
        var token = content[(separator + 1)..];
        if (token.Length == 0 || token[0] is not ('|' or '>') || !token.Skip(1).All(c => c is '+' or '-' || char.IsAsciiDigit(c)))
        {
            return false;
        }

        var before = separator < 0 ? string.Empty : content[..separator].TrimEnd();
        return before.Length == 0 || before[^1] is ':' or '-';
    }
}
