namespace Harness.Tests;

public sealed class DuplicationDefaultTests
{
    private const string Check = "duplication.csharp";

    [Fact]
    public void A_pin_before_the_recalibration_keeps_the_short_window_default()
    {
        using var repository = Repository(Frame.AllPresent().Version("1.3.0"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("lexically repeated block"), run.Output);
    }

    [Fact]
    public void The_recalibrated_default_ignores_the_same_short_block()
    {
        using var repository = Repository(Frame.AllPresent());

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("lexically repeated block"), run.Output);
    }

    private static RepositoryFixture Repository(Frame frame)
        => Fixtures.Compliant(frame)
            .WriteFile("src/App/First.cs", DuplicationSources.ShortBlock("First", "seed"))
            .WriteFile("src/App/Second.cs", DuplicationSources.ShortBlock("Second", "start"))
            .Commit();
}
