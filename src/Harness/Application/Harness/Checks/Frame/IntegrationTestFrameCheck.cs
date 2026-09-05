namespace Harness.Checks.Frame;

internal sealed class IntegrationTestFrameCheck : FrameQuestionCheck
{
    protected override string Key => "tests.integration";
    protected override bool AddressesTestProjects => true;
    protected override string Subject => "integration tests";
    public override string Summary => "repository answer about integration tests";
    public override string Explanation => FrameExplanations.IntegrationTests;
}
