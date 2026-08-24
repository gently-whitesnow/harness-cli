namespace Harness.Checks.Frame;

internal sealed class TypecheckFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "typecheck";
    protected override string Subject => "ahead-of-time type checking";
    public override string Summary => "repository answer about type checking";
    public override string Explanation => FrameExplanations.Typecheck;
}
