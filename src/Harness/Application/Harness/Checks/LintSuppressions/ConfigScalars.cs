namespace Harness.Checks.LintSuppressions;

/// <summary>The scalar forms YAML and TOML share: quoted strings, flow lists and `#` comments.</summary>
internal static class ConfigScalars
{
    public static void Assign(ConfigNode node, string value)
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
    public static string Unquote(string value)
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
    public static string StripComment(string line, char marker)
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
}
