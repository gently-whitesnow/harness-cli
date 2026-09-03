namespace Harness.Config;

/// <summary>
/// The diagnostics a repository has decided, once and for the whole tree, that it may
/// silence, each with the reason reviewers accepted. This is a repository-wide comparison
/// point like a threshold, not the address-level suppression ADR-0035 refuses: it names a
/// code, never a file or a finding.
/// </summary>
internal sealed record WarningSuppressionSettings(IReadOnlyDictionary<string, string> Allowed)
{
    public static WarningSuppressionSettings Default { get; } =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public bool Allows(string code) => Allowed.ContainsKey(code);
}
