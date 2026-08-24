namespace Harness.Checks.Frame;

internal sealed class UnitTestFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "tests.unit";
    protected override string Subject => "unit tests";
    public override string Summary => "repository answer about unit tests";
    public override string Explanation => FrameExplanations.UnitTests;
}
