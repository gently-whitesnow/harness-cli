namespace Harness.Infrastructure.Languages.Yaml;

/// <summary>
/// Counts the physical lines of a YAML document that carry prose. A `#` inside a quoted or
/// block scalar is content. A comment block above a key, an item or `---` documents that key
/// and leaves both counts, as a Go doc comment does; a trailing comment documents its value;
/// a tool directive is code. Prose is a block followed by another block or the end of the file.
/// </summary>
internal static class YamlCommentLines
{
    private static readonly string[] Directives = ["yamllint", "noqa", "ansible-lint"];

    private enum Kind
    {
        Blank,
        Code,
        Comment,
        Directive,
    }

    public static (int CommentLines, int AuthoredLines) Count(string text)
    {
        var kinds = Classify(text.Split('\n').Select(raw => raw.TrimEnd('\r')).ToList());
        var comments = 0;
        var authored = 0;
        for (var index = 0; index < kinds.Count; index++)
        {
            var kind = kinds[index].Kind;
            if (kind is Kind.Blank or Kind.Directive)
            {
                continue;
            }

            if (kind != Kind.Comment)
            {
                authored++;
                continue;
            }

            var end = index;
            while (end + 1 < kinds.Count && kinds[end + 1].Kind == Kind.Comment)
            {
                end++;
            }

            if (!DocumentsWhatFollows(kinds, end))
            {
                comments += end - index + 1;
                authored += end - index + 1;
            }

            index = end;
        }

        return (comments, authored);
    }

    // The next non-blank line after the block decides: a key, an item or `---` is documented
    // by it; another comment block or the end of the file leaves it as prose.
    private static bool DocumentsWhatFollows(List<(Kind Kind, bool Key)> kinds, int blockEnd)
    {
        for (var next = blockEnd + 1; next < kinds.Count; next++)
        {
            if (kinds[next].Kind != Kind.Blank)
            {
                return kinds[next].Kind == Kind.Code && kinds[next].Key;
            }
        }

        return false;
    }

    private static List<(Kind Kind, bool Key)> Classify(List<string> lines)
    {
        var kinds = new List<(Kind, bool)>(lines.Count);
        var blockIndent = -1;
        var quote = '\0';
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                kinds.Add((Kind.Blank, false));
                continue;
            }

            var indent = line.Length - line.TrimStart(' ').Length;
            if (blockIndent >= 0 && indent > blockIndent)
            {
                kinds.Add((Kind.Code, false));
                continue;
            }

            blockIndent = -1;
            var (commentAt, opensBlock) = Scan(line, ref quote);
            var content = (commentAt < 0 ? line : line[..commentAt]).Trim();
            if (opensBlock)
            {
                blockIndent = indent;
            }

            if (content.Length > 0)
            {
                kinds.Add((Kind.Code, OpensKey(content)));
            }
            else
            {
                kinds.Add((IsDirective(line[(commentAt + 1)..]) ? Kind.Directive : Kind.Comment, false));
            }
        }

        return kinds;
    }

    private static bool IsDirective(string comment)
    {
        var body = comment.TrimStart();
        return Directives.Any(directive => body.StartsWith(directive, StringComparison.Ordinal));
    }

    /// <summary>A mapping key, a sequence item or a document marker: the things a comment above can describe.</summary>
    private static bool OpensKey(string content)
        => content == "---" || content == "-" || content.StartsWith("- ", StringComparison.Ordinal) || KeyEnd(content) >= 0;

    // The colon that ends a key: followed by a space or the end, outside quotes.
    private static int KeyEnd(string content)
    {
        var quote = '\0';
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'' && index == 0)
            {
                quote = character;
            }
            else if (character == ':' && (index + 1 == content.Length || content[index + 1] == ' '))
            {
                return index;
            }
        }

        return -1;
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
