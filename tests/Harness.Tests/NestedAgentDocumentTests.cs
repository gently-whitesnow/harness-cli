namespace Harness.Tests;

/// <summary>
/// The names an agent opens by itself keep their meaning below the root: which nested
/// documents the policy allows, and which contract each of them still has to honour.
/// </summary>
public sealed class NestedAgentDocumentTests
{
    [Fact]
    public void Nested_overview_is_allowed()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/web/README.md", "# Web\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("apps/web/README.md"), run.Output);
    }

    [Fact]
    public void Nested_overview_over_the_line_limit_fails_the_run()
    {
        using var repository = Fixtures.Compliant()
            .WriteLines("apps/web/README.md", 151)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("apps/web/README.md"), run.Output);
        Assert.True(run.OutputContains("151"), run.Output);
    }

    [Fact]
    public void Nested_instruction_document_is_allowed_and_measured()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("specs/contracts/AGENTS.md", "# Contracts\n")
            .WriteLines("apps/api/AGENTS.md", 151)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.False(run.OutputContains("specs/contracts/AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("apps/api/AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("151"), run.Output);
    }

    [Fact]
    public void Nested_instruction_document_as_a_symbolic_link_fails_the_run()
    {
        using var repository = Fixtures.Compliant()
            .WriteSymbolicLink("apps/api/AGENTS.md", "../../AGENTS.md")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("apps/api/AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("link"), run.Output);
    }

    [Fact]
    public void Nested_entry_point_linked_to_its_sibling_is_allowed()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/AGENTS.md", "# Api\n")
            .WriteSymbolicLink("apps/api/CLAUDE.md", "AGENTS.md")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("apps/api/CLAUDE.md"), run.Output);
    }

    [Fact]
    public void Nested_entry_point_as_a_regular_file_fails_the_run()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/AGENTS.md", "# Api\n")
            .WriteFile("apps/api/CLAUDE.md", "# Api\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("apps/api/CLAUDE.md"), run.Output);
        Assert.True(run.OutputContains("regular file"), run.Output);
    }

    [Fact]
    public void Nested_entry_point_without_a_sibling_instruction_document_fails_the_run()
    {
        using var repository = Fixtures.Compliant()
            .WriteSymbolicLink("apps/api/CLAUDE.md", "AGENTS.md")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("apps/api/CLAUDE.md"), run.Output);
        Assert.True(run.OutputContains("broken"), run.Output);
    }

    [Fact]
    public void Nested_entry_point_reaching_for_the_root_document_fails_the_run()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/AGENTS.md", "# Api\n")
            .WriteSymbolicLink("apps/api/CLAUDE.md", "../../AGENTS.md")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("apps/api/CLAUDE.md"), run.Output);
        Assert.True(run.OutputContains("beside it"), run.Output);
    }

    [Fact]
    public void Skill_documents_are_allowed_at_any_depth_and_are_not_measured()
    {
        using var repository = Fixtures.Compliant()
            .WriteLines("skills/billing-report/SKILL.md", 282)
            .WriteLines(".claude/skills/billing-report/SKILL.md", 21)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("SKILL.md"), run.Output);
    }

    [Fact]
    public void A_differently_named_nested_document_is_still_unexpected()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("specs/AGENTS.local.md", "# Local\n")
            .WriteFile("ROOT.md", "# Root\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("specs/AGENTS.local.md"), run.Output);
        Assert.True(run.OutputContains("ROOT.md"), run.Output);
    }
}
