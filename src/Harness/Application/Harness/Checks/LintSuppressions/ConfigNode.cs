namespace Harness.Checks.LintSuppressions;

/// <summary>
/// One node of a configuration document read lexically from YAML, TOML or JSON: a scalar, a
/// sequence, or a mapping whose members keep document order. Three readers produce this one
/// shape so the rules that judge a golangci-lint configuration are written once.
/// </summary>
internal sealed class ConfigNode(int line)
{
    public int Line { get; } = line;

    public string? Scalar { get; set; }

    public List<ConfigNode>? Items { get; set; }

    public List<KeyValuePair<string, ConfigNode>>? Members { get; set; }

    public bool IsEmpty
        => Scalar is null or ""
            && (Items is null || Items.Count == 0)
            && (Members is null || Members.Count == 0);

    /// <summary>The member at a dotted path, or null when any segment is missing.</summary>
    public ConfigNode? Get(params string[] path)
    {
        var node = this;
        foreach (var segment in path)
        {
            node = node.Member(segment);
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    public ConfigNode? Member(string key)
        => Members?.FirstOrDefault(member => string.Equals(member.Key, key, StringComparison.Ordinal)).Value;

    public ConfigNode Child(string key)
    {
        var existing = Member(key);
        if (existing is not null)
        {
            return existing;
        }

        Members ??= [];
        var created = new ConfigNode(Line);
        Members.Add(new KeyValuePair<string, ConfigNode>(key, created));
        return created;
    }

    /// <summary>Scalar items of a sequence, or the scalar itself as one value.</summary>
    public IReadOnlyList<string> Values
        => Items is not null
            ? Items.Where(item => item.Scalar is not null).Select(item => item.Scalar!).ToList()
            : Scalar is { Length: > 0 } ? [Scalar] : [];

    public bool IsTrue => string.Equals(Scalar, "true", StringComparison.OrdinalIgnoreCase);
}
