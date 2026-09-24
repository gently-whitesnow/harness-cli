namespace Harness.Config;

internal sealed record AdrShapeSettings(int WordLimit, int FencedLineLimit, int TableRowLimit)
{
    public static AdrShapeSettings Default { get; } = new(1000, 10, 12);
}
