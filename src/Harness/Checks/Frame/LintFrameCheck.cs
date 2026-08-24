namespace Harness.Checks.Frame;

internal sealed class LintFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "lint";
    protected override string Subject => "static analysis";
    public override string Summary => "repository answer about static analysis";
    public override string Explanation => FrameExplanations.Lint;
}
