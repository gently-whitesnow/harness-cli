namespace Harness.Checks.Frame;

internal sealed class UnitTestFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "tests.unit";
    protected override string Subject => "unit tests";
    public override string Summary => "repository answer about unit tests";
    public override string Explanation => FrameExplanations.UnitTests;
}

internal sealed class IntegrationTestFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "tests.integration";
    protected override string Subject => "integration tests";
    public override string Summary => "repository answer about integration tests";
    public override string Explanation => FrameExplanations.IntegrationTests;
}

internal sealed class ArchitectureFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "tests.architecture";
    protected override string Subject => "architecture rules";
    public override string Summary => "repository answer about architecture rules";
    public override string Explanation => FrameExplanations.Architecture;
}

internal sealed class FormatFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "format";
    protected override string Subject => "source formatting";
    public override string Summary => "repository answer about source formatting";
    public override string Explanation => FrameExplanations.Format;
}

internal sealed class LintFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "lint";
    protected override string Subject => "static analysis";
    public override string Summary => "repository answer about static analysis";
    public override string Explanation => FrameExplanations.Lint;
}

internal sealed class BuildFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "build";
    protected override string Subject => "the build entry point";
    public override string Summary => "repository answer about its build entry point";
    public override string Explanation => FrameExplanations.Build;
}

internal sealed class TypecheckFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "typecheck";
    protected override string Subject => "ahead-of-time type checking";
    public override string Summary => "repository answer about type checking";
    public override string Explanation => FrameExplanations.Typecheck;
}
