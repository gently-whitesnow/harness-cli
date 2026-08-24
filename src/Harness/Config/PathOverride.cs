namespace Harness.Config;

/// <summary>
/// A zone norm: for the tracked paths its globs cover, one check either reads different
/// numbers or does not analyze the zone at all. Unlike a suppression it acts before
/// measurement, so it never accepts a finding — it changes what would count as one.
/// </summary>
internal sealed record PathOverride(
    string Check,
    IReadOnlyList<string> Paths,
    string Reason,
    bool Off,
    IReadOnlyDictionary<string, int> Settings)
{
    public bool Covers(string checkId, string path)
        => string.Equals(Check, checkId, StringComparison.Ordinal)
            && Paths.Any(pattern => PathGlob.Matches(pattern, path));
}
