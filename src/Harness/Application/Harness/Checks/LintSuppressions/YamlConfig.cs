namespace Harness.Checks.LintSuppressions;

/// <summary>
/// Reads a YAML configuration into <see cref="ConfigNode"/> without a grammar: mappings by
/// indentation, sequences by `- ` items, one document. It knows the shapes linter
/// configurations use — golangci-lint, ansible-lint — and nothing more, so an anchor, a merge
/// key or a flow mapping reads as absent rather than as a finding.
/// </summary>
internal static class YamlConfig
{
    public static ConfigNode Parse(string text)
    {
        var lines = text.Split('\n')
            .Select((raw, index) => (Line: index + 1, Text: ConfigScalars.StripComment(raw.TrimEnd('\r'), '#').TrimEnd()))
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

            var key = ConfigScalars.Unquote(line.Content[..colon].Trim());
            var rest = line.Content[(colon + 1)..].Trim();
            var node = new ConfigNode(line.Line);
            cursor++;
            if (rest.Length > 0)
            {
                ConfigScalars.Assign(node, rest);
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
                ConfigScalars.Assign(item, content);
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

    private sealed record YamlLine(int Line, int Indent, string Content)
    {
        public bool IsItem => Content.StartsWith('-') && (Content.Length == 1 || Content[1] == ' ');
    }
}
