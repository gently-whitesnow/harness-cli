namespace Harness.Config;

internal sealed record DuplicationSettings(int WindowLines, int MinimumTokens)
{
    public static DuplicationSettings Default { get; } = new(WindowLines: 8, MinimumTokens: 24);
}
