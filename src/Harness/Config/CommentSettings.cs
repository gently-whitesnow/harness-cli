namespace Harness.Config;

internal sealed record CommentSettings(int MinimumCommentLines, int PercentageLimit)
{
    public static CommentSettings Default { get; } = new(10, 25);
}
