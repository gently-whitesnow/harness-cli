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
        Assert.True(run.OutputContains("harness upgrade"), run.Output);
    }

    [Fact]
    public void Upgrade_raises_an_old_pin_and_prints_the_complete_2_0_migration()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version("1.5.0"));

        var run = HarnessCli.Run(repository.Path, "upgrade");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains($"from 1.5.0 to {Release.Current}", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("removed  maintainability.csharp, cohesion.csharp", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("sliced-dotnet/1", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("explicit applicability, settings and policy", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains(".harness.budget.json", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("duplication.csharp as required", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("answers.verify", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("nearest .csproj is a", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains($"\"version\": \"{Release.Current}\"", File.ReadAllText(repository.Absolute(".harness.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Upgrade_dry_run_describes_the_migration_without_changing_the_pin()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version("1.5.0"));
        var before = File.ReadAllText(repository.Absolute(".harness.json"));

        var run = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Nothing was written", run.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(repository.Absolute(".harness.json")));
    }
}
