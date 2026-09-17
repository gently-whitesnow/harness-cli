namespace Harness.Tests;

/// <summary>ADR-0062: one command tells an agent how to work with the harness.</summary>
public sealed class GuideCommandTests
{
    [Fact]
    public void Guide_prints_the_working_loop_without_a_frame()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.Run(repository.Path, "guide");

        Assert.Equal(0, run.ExitCode);
        foreach (var expected in new[]
        {
            "harness check --only <id> --verbose",
            "harness explain <id>",
            "not the frame",
            "required", "advisory", "answers", "settings",
            "Exit codes",
            "harness commit-message template",
            "harness setup",
            "harness upgrade",
        })
        {
            Assert.True(run.OutputContains(expected), $"missing '{expected}':\n{run.Output}");
        }
    }

    [Fact]
    public void Guide_stays_short_enough_for_an_agent_context()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.Run(repository.Path, "guide");

        Assert.InRange(run.Output.Split('\n').Length, 20, 60);
    }

    [Fact]
    public void Guide_is_listed_in_help_and_refuses_arguments()
    {
        using var repository = Fixtures.Compliant();

        Assert.True(HarnessCli.Run(repository.Path, "help").OutputContains("harness guide"));
        Assert.Equal(2, HarnessCli.Run(repository.Path, "guide", "extra").ExitCode);
    }
}
