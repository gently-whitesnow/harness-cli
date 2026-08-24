namespace Harness.Config;

internal sealed record CommentSettings(int MinimumCommentLines, int PercentageLimit)
{
    public static CommentSettings Default { get; } = new(10, 25);

    public CommentSettings With(string name, int value) => name switch
    {
        "minimumCommentLines" => this with { MinimumCommentLines = value },
        "percentageLimit" => this with { PercentageLimit = value },
        _ => this,
    };
}
