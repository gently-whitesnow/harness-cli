namespace Harness.Tests;

/// <summary>AGENTS.md and CLAUDE.md must be direct relative Git symbolic links to ROOT.md.</summary>
public sealed class AgentEntryPointTests
{
    [Fact]
    public void Direct_relative_link_written_as_dot_slash_passes()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("ROOT.md", "# Root\n")
            .WriteSymbolicLink("AGENTS.md", "./ROOT.md")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Missing_agent_entry_point_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("ROOT.md", "# Root\n")
            .WriteSymbolicLink("AGENTS.md", "ROOT.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("CLAUDE.md"), run.Output);
    }

    [Fact]
    public void Regular_file_copy_is_a_violation()
    {
        using var repository = Fixtures.Compliant();
        File.Delete(repository.Absolute("AGENTS.md"));
        repository.WriteFile("AGENTS.md", "# Root\n").Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("regular file"), run.Output);
    }

    [Fact]
    public void Chained_link_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("ROOT.md", "# Root\n")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .WriteSymbolicLink("AGENTS.md", "CLAUDE.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("chained"), run.Output);
    }

    [Fact]
    public void Broken_link_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("ROOT.md", "# Root\n")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .WriteSymbolicLink("AGENTS.md", "docs/ROOT.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("broken"), run.Output);
    }

    [Fact]
    public void Absolute_link_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("ROOT.md", "# Root\n")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .Commit();
        repository.WriteSymbolicLink("AGENTS.md", repository.Absolute("ROOT.md")).Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("absolute"), run.Output);
    }

    [Fact]
    public void Link_to_a_different_tracked_document_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("ROOT.md", "# Root\n")
            .WriteFile("docs/ROOT.md", "# Other root\n")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .WriteSymbolicLink("AGENTS.md", "docs/ROOT.md")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("docs/ROOT.md"), run.Output);
    }
}
