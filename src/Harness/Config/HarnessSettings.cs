namespace Harness.Config;

internal sealed record CommentSettings(int MinimumCommentLines, int PercentageLimit)
{
    public static CommentSettings Default { get; } = new(10, 25);
}

internal sealed record MaintainabilitySettings(
    int FileLines,
    int TypeLines,
    int MethodLines,
    int Branches,
    int ConstructorParameters,
    int PublicMembers,
    int ImportFanOut)
{
    public static MaintainabilitySettings Default { get; } = new(
        FileLines: 400,
        TypeLines: 300,
        MethodLines: 60,
        Branches: 12,
        ConstructorParameters: 6,
        PublicMembers: 25,
        ImportFanOut: 20);
}

internal sealed record HarnessSettings(
    CommentSettings Comments,
    MaintainabilitySettings Maintainability)
{
    public static HarnessSettings Default { get; } = new(
        CommentSettings.Default,
        MaintainabilitySettings.Default);
}
