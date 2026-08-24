namespace Harness.Checks.Frame;

internal sealed class IntegrationTestFrameCheck : FrameQuestionCheck
{
    internal override int IntroducedIn => 2;
    protected override string Key => "tests.integration";
    protected override string Subject => "integration tests";
    public override string Summary => "repository answer about integration tests";
    public override string Explanation => FrameExplanations.IntegrationTests;
}
