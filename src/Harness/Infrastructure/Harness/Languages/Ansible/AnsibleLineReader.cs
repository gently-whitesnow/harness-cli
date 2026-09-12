using Harness.Languages.Ansible;

namespace Harness.Infrastructure.Languages.Ansible;

/// <summary>
/// Reads YAML into <see cref="AnsibleLine"/>s without a grammar: a key is
/// the text before the first `: ` outside quotes, a comment is `#` after whitespace outside
/// quotes, a `- ` opens an item, and the body of a block scalar folds into the value of the
/// line that opened it. Anchors, merge keys and flow mappings read as plain values.
/// </summary>
internal static class AnsibleLineReader
{
    public static List<AnsibleLine> Read(string text)
    {
        var lines = new List<AnsibleLine>();
        var blockIndent = -1;
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart(' ').Length;
            if (blockIndent >= 0 && indent > blockIndent)
            {
                lines[^1] = Fold(lines[^1], line.Trim());
                continue;
            }

            blockIndent = -1;
            var parsed = Parse(number, indent, line);
            lines.Add(parsed);
            if (OpensBlockScalar(parsed.Value))
            {
                blockIndent = indent;
            }
        }

        return lines;
    }

    private static AnsibleLine Parse(int number, int indent, string line)
    {
        var (content, comment) = Split(line);
        var isItem = content == "-" || content.StartsWith("- ", StringComparison.Ordinal);
        if (isItem)
        {
            // The item's own keys start where the text after `- ` starts.
            var body = content[1..];
            indent = line.IndexOf('-') + 1 + (body.Length - body.TrimStart().Length);
            content = body.Trim();
        }

        var colon = content.StartsWith('{') || content.StartsWith('[') ? -1 : KeyEnd(content);
        return colon < 0
            ? new AnsibleLine(number, indent, isItem, null, content, comment)
            : new AnsibleLine(number, indent, isItem, Unquote(content[..colon].Trim()), content[(colon + 1)..].Trim(), comment);
    }

    // The first block line replaces the `|` or `>` indicator; later lines join with a space.
    private static AnsibleLine Fold(AnsibleLine opener, string body)
    {
        var value = opener.Value;
        var indicator = value.LastIndexOf(' ') is var space && space >= 0 ? value[(space + 1)..] : value;
        if (indicator.Length > 0 && indicator[0] is '|' or '>')
        {
            value = space >= 0 ? value[..space] : string.Empty;
        }

        return opener with { Value = (value + " " + body).Trim() };
    }

    private static (string Content, string? Comment) Split(string line)
    {
        var quote = '\0';
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quote != '\0')
            {
                if (character == '\\' && quote == '"')
                {
                    index++;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }
            }
            else if (character is '"' or '\'' && (index == 0 || line[index - 1] is ' ' or ':' or '[' or '{' or ','))
            {
                quote = character;
            }
            else if (character == '#' && (index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                return (line[..index].Trim(), line[(index + 1)..].Trim());
            }
        }

        return (line.Trim(), null);
    }

    private static int KeyEnd(string content)
    {
        var quoted = content.Length > 0 && content[0] is '"' or '\'';
        var close = quoted ? content.IndexOf(content[0], 1) : -1;
        if (quoted && close < 0)
        {
            return -1;
        }

        var colon = content.IndexOf(':', close + 1);
        while (colon >= 0 && colon + 1 < content.Length && content[colon + 1] != ' ')
        {
            colon = content.IndexOf(':', colon + 1);
        }

        return colon;
    }

    private static string Unquote(string key)
        => key.Length >= 2 && key[0] is '"' or '\'' && key[^1] == key[0] ? key[1..^1] : key;

    private static bool OpensBlockScalar(string value)
    {
        var token = value.LastIndexOf(' ') is var space && space >= 0 ? value[(space + 1)..] : value;
        return token.Length > 0 && token[0] is '|' or '>' && token.Skip(1).All(c => c is '+' or '-' || char.IsAsciiDigit(c));
    }
}
