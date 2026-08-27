namespace Harness.Checks.Frame;

internal sealed class ArchitectureFrameCheck : FrameQuestionCheck
{
    protected override string Key => "tests.architecture";
    protected override string Subject => "architecture rules";
    public override string Summary => "repository answer about architecture rules";
    public override string Explanation => FrameExplanations.Architecture;
}
