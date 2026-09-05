namespace Harness.Checks.Frame;

internal sealed class BuildFrameCheck : FrameQuestionCheck
{
    protected override string Key => "build";
    protected override string Subject => "the build entry point";
    public override string Summary => "repository answer about its build entry point";
    public override string Explanation => FrameExplanations.Build;
}
