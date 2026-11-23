namespace Harness.Tests;

public sealed class CheckCommandTests
{
    [Fact]
    public void Compliant_repository_passes()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.Output.StartsWith("PASS", StringComparison.Ordinal), run.Output);
    }

    [Fact]
    public void Missing_root_document_is_a_violation()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("README.md", "# Overview\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
    }

    [Fact]
    public void Root_document_at_the_word_limit_passes()
    {
        var words = string.Join(" ", Enumerable.Range(1, 1200).Select(n => $"word{n}"));
        using var repository = Fixtures.Compliant().WriteFile("AGENTS.md", words + "\n").Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Root_document_above_the_word_limit_is_a_violation()
    {
        var words = string.Join(" ", Enumerable.Range(1, 1201).Select(n => $"word{n}"));
        using var repository = Fixtures.Compliant().WriteFile("AGENTS.md", words + "\n").Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("1201"), run.Output);
        Assert.True(run.OutputContains("1200"), run.Output);
    }

    [Fact]
    public void Readme_at_the_word_limit_passes()
    {
        var words = string.Join(" ", Enumerable.Range(1, 1200).Select(n => $"word{n}"));
        using var repository = Fixtures.Compliant().WriteFile("README.md", words + "\n").Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Readme_above_the_word_limit_is_a_violation()
    {
        var words = string.Join(" ", Enumerable.Range(1, 1201).Select(n => $"word{n}"));
        using var repository = Fixtures.Compliant().WriteFile("README.md", words + "\n").Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("README.md"), run.Output);
        Assert.True(run.OutputContains("1201"), run.Output);
        Assert.True(run.OutputContains("1200"), run.Output);
    }

    [Fact]
    public void Missing_readme_is_allowed()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("AGENTS.md", "# Root\n")
            .WriteSymbolicLink("CLAUDE.md", "AGENTS.md")
            .WriteExecutable("verify.sh", "#!/usr/bin/env sh\nharness check\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void A_tracked_document_deleted_from_the_working_tree_is_judged_by_its_staged_content()
    {
        using var repository = Fixtures.Compliant().WriteLines("AGENTS.md", 10).Commit();
        File.Delete(repository.Absolute("AGENTS.md"));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        // The staged content is still readable evidence, so this is neither a violation
        // of the line limit nor an unverifiable run.
        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Untracked_root_document_does_not_satisfy_the_policy()
    {
        using var repository = Fixtures.Framed()
            .WriteFile("README.md", "# Overview\n")
            .Commit()
            .WriteFile("AGENTS.md", "# Root\n");

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
    }
}
