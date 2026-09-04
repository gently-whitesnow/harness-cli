namespace Harness.Versioning;

/// <summary>
/// A harness release, which is also the contract a repository names in its frame.
/// </summary>
internal readonly record struct HarnessVersion(int Major, int Minor, int Patch)
{
    private const int Components = 3;

    /// <summary>The release this binary is.</summary>
    public static HarnessVersion Current { get; } = Parse(HarnessBuild.Version);

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
}
