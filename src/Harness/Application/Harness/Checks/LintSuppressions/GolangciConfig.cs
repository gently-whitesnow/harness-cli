using System.Text.Json;

namespace Harness.Checks.LintSuppressions;

/// <summary>
/// Reads a golangci-lint configuration into <see cref="ConfigNode"/> without a grammar: YAML
/// through the shared <see cref="YamlConfig"/> reader, TOML by tables and `key = value`, JSON
/// through the BCL. Each reader knows the shapes golangci-lint documents use and nothing more,
/// so an exotic layout reads as absent rather than as a finding.
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

        return path.EndsWith(".toml", StringComparison.Ordinal) ? (Toml(text), null) : (YamlConfig.Parse(text), null);
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
            var line = ConfigScalars.StripComment(lines[index].TrimEnd('\r'), '#').Trim();
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
            var keyLine = index + 1;
            // An array may continue on the following lines until its bracket closes; the key's line is the address.
            while (value.StartsWith('[') && value.Count(c => c == '[') > value.Count(c => c == ']') && index + 1 < lines.Length)
            {
                index++;
                value += " " + ConfigScalars.StripComment(lines[index].TrimEnd('\r'), '#').Trim();
            }

            var target = Walk(current, line[..equals].Trim(), keyLine);
            ConfigScalars.Assign(target, value);
        }

        return root;
    }

    private static ConfigNode Walk(ConfigNode from, string dotted, int line, bool descendLast = true)
    {
        var node = from;
        var segments = dotted.Split('.', StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var key = ConfigScalars.Unquote(segments[index]);
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
}
