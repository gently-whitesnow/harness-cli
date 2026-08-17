namespace Harness.Tests;

/// <summary>CLAUDE.md must be a direct relative Git symbolic link to the tracked AGENTS.md.</summary>
public sealed class AgentEntryPointTests
{
    [Fact]
    public void Direct_relative_link_written_as_dot_slash_passes()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("AGENTS.md", "# Root\n")
            .WriteSymbolicLink("CLAUDE.md", "./AGENTS.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Missing_agent_entry_point_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("AGENTS.md", "# Root\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("CLAUDE.md"), run.Output);
    }

    [Fact]
    public void Regular_file_copy_is_a_violation()
    {
        using var repository = Fixtures.Compliant();
        File.Delete(repository.Absolute("CLAUDE.md"));
        repository.WriteFile("CLAUDE.md", "# Root\n").Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("CLAUDE.md"), run.Output);
        Assert.True(run.OutputContains("regular file"), run.Output);
    }

    [Fact]
    public void A_root_document_that_is_itself_a_link_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("docs/navigation.md", "# Root\n")
            .WriteSymbolicLink("AGENTS.md", "docs/navigation.md")
            .WriteSymbolicLink("CLAUDE.md", "AGENTS.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("symbolic link"), run.Output);
    }

    [Fact]
    public void Chained_link_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("AGENTS.md", "# Root\n")
            .WriteSymbolicLink("NAVIGATION.md", "AGENTS.md")
            .WriteSymbolicLink("CLAUDE.md", "NAVIGATION.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("CLAUDE.md"), run.Output);
        Assert.True(run.OutputContains("chained"), run.Output);
    }

    [Fact]
    public void Broken_link_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("AGENTS.md", "# Root\n")
            .WriteSymbolicLink("CLAUDE.md", "docs/AGENTS.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("CLAUDE.md"), run.Output);
        Assert.True(run.OutputContains("broken"), run.Output);
    }

    [Fact]
    public void Absolute_link_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("AGENTS.md", "# Root\n")
            .Commit();
        repository.WriteSymbolicLink("CLAUDE.md", repository.Absolute("AGENTS.md")).Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("CLAUDE.md"), run.Output);
        Assert.True(run.OutputContains("absolute"), run.Output);
    }

    [Fact]
    public void Link_to_a_different_tracked_document_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("AGENTS.md", "# Root\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("CLAUDE.md", "README.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("CLAUDE.md"), run.Output);
        Assert.True(run.OutputContains("README.md"), run.Output);
    }
}
