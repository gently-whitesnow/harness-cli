namespace Harness.Config;

internal sealed record AdrShapeSettings(int FencedBlockLineLimit)
{
    public static AdrShapeSettings Default { get; } = new(20);
}
