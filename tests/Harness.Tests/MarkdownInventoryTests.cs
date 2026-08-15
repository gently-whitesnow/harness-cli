namespace Harness.Tests;

/// <summary>Which Markdown the policy considers, and which of it is merely advisory.</summary>
public sealed class MarkdownInventoryTests
{
    [Fact]
    public void Markdown_under_the_root_adrs_directory_is_allowed()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("adrs/0001-use-a-harness.md", "# Decision\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("0001-use-a-harness.md"), run.Output);
    }

    [Fact]
    public void Other_tracked_markdown_is_advisory_and_does_not_fail_the_run()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("docs/old-specification.md", "# Stale\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("advisory"), run.Output);
        Assert.True(run.OutputContains("docs/old-specification.md"), run.Output);
    }

    [Fact]
    public void Markdown_under_a_nested_adrs_directory_is_advisory()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("services/billing/adrs/0001-queue.md", "# Decision\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("services/billing/adrs/0001-queue.md"), run.Output);
    }

    [Fact]
    public void Many_advisory_documents_stay_bounded_in_the_default_output()
    {
        using var repository = Fixtures.Compliant();
        for (var index = 0; index < 40; index++)
        {
            repository.WriteFile($"docs/note-{index:00}.md", "# Note\n");
        }

        repository.Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= 20, run.Output);
        Assert.True(run.OutputContains("40"), run.Output);
    }

    [Fact]
    public void Untracked_markdown_is_ignored()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("scratch/notes.md", "# Notes\n");

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("scratch/notes.md"), run.Output);
    }

    [Fact]
    public void Vendored_and_build_output_markdown_is_ignored()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("node_modules/left-pad/README.md", "# left-pad\n")
            .WriteFile("web/dist/report.md", "# Generated\n")
            .WriteFile("src/Service/obj/notes.md", "# Build output\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("left-pad"), run.Output);
        Assert.False(run.OutputContains("web/dist/report.md"), run.Output);
        Assert.False(run.OutputContains("src/Service/obj/notes.md"), run.Output);
    }

    [Fact]
    public void Non_markdown_contracts_are_out_of_scope()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("contracts/openapi.yaml", "openapi: 3.1.0\n")
            .WriteFile("contracts/event.schema.json", "{}\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("openapi.yaml"), run.Output);
        Assert.False(run.OutputContains("event.schema.json"), run.Output);
    }
}
