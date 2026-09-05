namespace Harness.Checks.Frame;

internal sealed class VerifyFrameCheck : FrameQuestionCheck
{
    protected override string Key => "verify";
    protected override string Subject => "the repository verification entry point";
    protected override bool RequiresLocation => true;
    protected override bool AppliesToEveryRepository => true;
    public override string Summary => "repository answer about its unified verification script";
    public override string Explanation => FrameExplanations.Verify;
}
