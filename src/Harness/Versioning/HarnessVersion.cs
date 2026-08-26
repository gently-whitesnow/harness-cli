namespace Harness.Versioning;

/// <summary>
/// A harness release, which is also the contract a repository pins in its frame. Behaviour
/// changes only on minor and major, so releases differing in patch reach the same verdict on
/// the same pin, and a patch component in a pin carries provenance rather than meaning.
/// </summary>
internal readonly record struct HarnessVersion(int Major, int Minor, int Patch)
{
    private const int Components = 3;

    /// <summary>The release this binary is.</summary>
    public static HarnessVersion Current { get; } = Parse(HarnessBuild.Version);

    /// <summary>The first release, and so the oldest possible origin of a shipped check.</summary>
    public static HarnessVersion Initial { get; } = new(1, 0, 0);

    /// <summary>The only contract this binary implements.</summary>
    public static HarnessVersion Minimum => Current;

    public static bool operator <(HarnessVersion left, HarnessVersion right) => Compare(left, right) < 0;

    public static bool operator >(HarnessVersion left, HarnessVersion right) => Compare(left, right) > 0;

    public static bool operator <=(HarnessVersion left, HarnessVersion right) => Compare(left, right) <= 0;

    public static bool operator >=(HarnessVersion left, HarnessVersion right) => Compare(left, right) >= 0;

    public static bool TryParse(string? text, out HarnessVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text.Split('.');
        if (parts.Length != Components)
        {
            return false;
        }

        var numbers = new int[Components];
        for (var index = 0; index < Components; index++)
        {
            if (!IsDigits(parts[index]) || !int.TryParse(parts[index], out numbers[index]))
            {
                return false;
            }
        }

        version = new HarnessVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    private static HarnessVersion Parse(string text)
        => TryParse(text, out var version)
            ? version
            : throw new InvalidOperationException($"The build stamped an unreadable version '{text}'.");

    // int.TryParse alone accepts '+1' and ' 1' — spellings that compare equal but read apart.
    private static bool IsDigits(string part)
        => part.Length > 0 && part.All(char.IsAsciiDigit);

    private static int Compare(HarnessVersion left, HarnessVersion right)
    {
        var major = left.Major.CompareTo(right.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = left.Minor.CompareTo(right.Minor);
        return minor != 0 ? minor : left.Patch.CompareTo(right.Patch);
    }
}
