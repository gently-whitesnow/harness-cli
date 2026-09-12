using System.Text.Json;

namespace Harness.Checks.LintSuppressions;

/// <summary>
/// Reads a golangci-lint configuration into <see cref="ConfigNode"/> without a grammar: YAML by
/// indentation and `- ` items, TOML by tables and `key = value`, JSON through the BCL. Each
/// reader knows the shapes golangci-lint documents use and nothing more, so an exotic layout
/// reads as absent rather than as a finding.
/// </summary>
internal static class GolangciConfig
{
    public static readonly IReadOnlyList<string> FileNames =
        [".golangci.yml", ".golangci.yaml", ".golangci.toml", ".golangci.json"];

    public static (ConfigNode? Root, string? Failure) Parse(string path, string text)
    {
        if (path.EndsWith(".json", StringComparison.Ordinal))
        {
            return Json(path, text);
        }

        return path.EndsWith(".toml", StringComparison.Ordinal) ? (Toml(text), null) : (Yaml(text), null);
    }

    private static (ConfigNode? Root, string? Failure) Json(string path, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return (FromJson(document.RootElement), null);
        }
        catch (JsonException exception)
        {
            return (null, $"'{path}' is not readable as JSON ({exception.Message}).");
        }
    }

    // JSON carries no line numbers through the BCL reader; the file is the address.
    private static ConfigNode FromJson(JsonElement element)
    {
        var node = new ConfigNode(0);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                node.Members = element.EnumerateObject()
                    .Select(property => new KeyValuePair<string, ConfigNode>(property.Name, FromJson(property.Value)))
                    .ToList();
                break;

            case JsonValueKind.Array:
                node.Items = element.EnumerateArray().Select(FromJson).ToList();
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                break;

            default:
                node.Scalar = element.ToString();
                break;
        }

        return node;
    }

    private static ConfigNode Toml(string text)
    {
        var root = new ConfigNode(1);
        var current = root;
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index].TrimEnd('\r'), '#').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
            {
                var table = Walk(root, line[2..^2], index + 1, descendLast: false);
                table.Items ??= [];
                current = new ConfigNode(index + 1);
                table.Items.Add(current);
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = Walk(root, line[1..^1], index + 1);
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            var value = line[(equals + 1)..].Trim();
            // An array may continue on the following lines until its bracket closes.
            while (value.StartsWith('[') && value.Count(c => c == '[') > value.Count(c => c == ']') && index + 1 < lines.Length)
            {
                index++;
                value += " " + StripComment(lines[index].TrimEnd('\r'), '#').Trim();
            }

            var target = Walk(current, line[..equals].Trim(), index + 1);
            Assign(target, value);
        }

        return root;
    }

    private static ConfigNode Walk(ConfigNode from, string dotted, int line, bool descendLast = true)
    {
        var node = from;
        var segments = dotted.Split('.', StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var key = Unquote(segments[index]);
            var next = node.Member(key);
            if (next is null)
            {
                next = new ConfigNode(line);
                node.Members ??= [];
                node.Members.Add(new KeyValuePair<string, ConfigNode>(key, next));
            }

            // A table array addressed again continues its last item; `[[...]]` itself opens a new one.
            var continues = descendLast || index + 1 < segments.Length;
            node = continues && next.Items is { Count: > 0 } && next.Scalar is null && next.Members is null ? next.Items[^1] : next;
        }

        return node;
    }

    private static ConfigNode Yaml(string text)
    {
        var lines = text.Split('\n')
            .Select((raw, index) => (Line: index + 1, Text: StripComment(raw.TrimEnd('\r'), '#').TrimEnd()))
            .Where(entry => entry.Text.Trim().Length > 0 && entry.Text.Trim() != "---")
            .Select(entry => new YamlLine(entry.Line, entry.Text.Length - entry.Text.TrimStart(' ').Length, entry.Text.Trim()))
            .ToList();
        var cursor = 0;
        var root = new ConfigNode(1);
        if (lines.Count > 0)
        {
            root.Members = ReadMapping(lines, ref cursor, lines[0].Indent);
        }

        return root;
    }

    private static List<KeyValuePair<string, ConfigNode>> ReadMapping(List<YamlLine> lines, ref int cursor, int indent)
    {
        var members = new List<KeyValuePair<string, ConfigNode>>();
        while (cursor < lines.Count && lines[cursor].Indent == indent && !lines[cursor].IsItem)
        {
            var line = lines[cursor];
            var colon = KeyEnd(line.Content);
            if (colon < 0)
            {
                cursor++;
                continue;
            }

            var key = Unquote(line.Content[..colon].Trim());
            var rest = line.Content[(colon + 1)..].Trim();
            var node = new ConfigNode(line.Line);
            cursor++;
            if (rest.Length > 0)
            {
                Assign(node, rest);
            }
            else if (cursor < lines.Count && (lines[cursor].Indent > indent || (lines[cursor].Indent == indent && lines[cursor].IsItem)))
            {
                if (lines[cursor].IsItem)
                {
                    node.Items = ReadSequence(lines, ref cursor, lines[cursor].Indent);
                }
                else
                {
                    node.Members = ReadMapping(lines, ref cursor, lines[cursor].Indent);
                }
            }

            members.Add(new KeyValuePair<string, ConfigNode>(key, node));
        }

        // Lines deeper than this mapping that no key claimed are skipped, never misread.
        while (cursor < lines.Count && lines[cursor].Indent > indent)
        {
            cursor++;
        }

        return members;
    }

    private static List<ConfigNode> ReadSequence(List<YamlLine> lines, ref int cursor, int indent)
    {
        var items = new List<ConfigNode>();
        while (cursor < lines.Count && lines[cursor].Indent == indent && lines[cursor].IsItem)
        {
            var line = lines[cursor];
            var content = line.Content[1..].TrimStart();
            var item = new ConfigNode(line.Line);
            if (content.Length > 0 && KeyEnd(content) >= 0)
            {
                // `- key: value` opens a mapping whose further keys sit two columns deeper.
                lines[cursor] = new YamlLine(line.Line, indent + 2, content);
                item.Members = ReadMapping(lines, ref cursor, indent + 2);
            }
            else
            {
                Assign(item, content);
                cursor++;
                while (cursor < lines.Count && lines[cursor].Indent > indent)
                {
                    cursor++;
                }
            }

            items.Add(item);
        }

        return items;
    }

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

    private static void Assign(ConfigNode node, string value)
    {
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            node.Items = value[1..^1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => new ConfigNode(node.Line) { Scalar = Unquote(item) })
                .ToList();
            return;
        }

        node.Scalar = value.StartsWith('|') || value.StartsWith('>') ? string.Empty : Unquote(value);
    }

    // A double-quoted string knows `\\` and `\"` in YAML, TOML and JSON alike; a single-quoted one is literal.
    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] is not ('"' or '\'') || value[^1] != value[0])
        {
            return value;
        }

        var inner = value[1..^1];
        return value[0] == '"'
            ? inner.Replace("\\\\", "\\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal)
            : inner;
    }

    // A `#` outside quotes, at the start or after whitespace, opens a comment in both YAML and TOML.
    private static string StripComment(string line, char marker)
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

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == marker && (index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                return line[..index];
            }
        }

        return line;
    }

    private sealed record YamlLine(int Line, int Indent, string Content)
    {
        public bool IsItem => Content.StartsWith('-') && (Content.Length == 1 || Content[1] == ' ');
    }
}
