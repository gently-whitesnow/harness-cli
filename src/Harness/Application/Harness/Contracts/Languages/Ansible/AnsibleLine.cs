namespace Harness.Languages.Ansible;

/// <summary>
/// One non-empty line of YAML or a Jinja template, read lexically: its physical number, the
/// column its key (or content) starts in, whether it opens a sequence item, the mapping key it
/// carries, the value after the key with its trailing comment removed, and that comment.
/// </summary>
internal sealed record AnsibleLine(int Number, int Indent, bool IsItem, string? Key, string Value, string? Comment)
{
    public bool IsComment => Key is null && Value.Length == 0 && Comment is not null;

    /// <summary>The value with one pair of matching quotes removed; a `"` string knows `\\` and `\"`.</summary>
    public string Scalar
    {
        get
        {
            if (Value.Length < 2 || Value[0] is not ('"' or '\'') || Value[^1] != Value[0])
            {
                return Value;
            }

            var inner = Value[1..^1];
            return Value[0] == '"'
                ? inner.Replace("\\\\", "\\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal)
                : inner;
        }
    }

    public bool IsOpaque => Value.Length > 0 && Value[0] is '&' or '*' or '[' or '{';

    public bool IsJinja => Value.Contains("{{", StringComparison.Ordinal);
}
