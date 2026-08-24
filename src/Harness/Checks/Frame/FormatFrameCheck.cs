namespace Harness.Checks.Frame;

internal sealed class FormatFrameCheck : FrameQuestionCheck
{
    protected override string Key => "format";
    protected override string Subject => "source formatting";
    public override string Summary => "repository answer about source formatting";
    public override string Explanation => FrameExplanations.Format;
}
