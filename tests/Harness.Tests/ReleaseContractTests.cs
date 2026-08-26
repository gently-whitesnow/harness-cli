namespace Harness.Tests;

/// <summary>
/// The binary evaluates exactly its current contract and rejects every other pin.
/// </summary>
[Trait("Category", "Publication")]
public sealed class ReleaseContractTests
{
    [Fact]
    public void Version_reports_the_release_the_build_stamped()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "version");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith($"harness {Release.Current}", run.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_names_the_binary_and_the_pin_it_ran()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains($"harness {Release.Current}", run.Output, StringComparison.Ordinal);
        Assert.Contains($"repository pins {Release.Current}", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.5.0")]
    [InlineData("99.0.0")]
    public void A_pin_that_differs_from_the_current_contract_requires_an_upgrade(string pin)
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version(pin));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("upgrade required"), run.Output);
        Assert.True(run.OutputContains($"only runs contract {Release.Current}"), run.Output);
    }

}
