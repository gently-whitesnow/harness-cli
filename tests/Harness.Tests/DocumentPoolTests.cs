namespace Harness.Tests;

/// <summary>
/// The built-in pool of ADR-0061: the Markdown a forge reads by itself is allowed only where
/// the forge looks for it, a design document is allowed by name, and neither is measured.
/// </summary>
public sealed class DocumentPoolTests
{
    [Theory]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("CHANGELOG.md")]
    [InlineData(".github/SECURITY.md")]
    [InlineData("docs/CODE_OF_CONDUCT.md")]
    [InlineData("docs/SUPPORT.md")]
    [InlineData(".github/GOVERNANCE.md")]
    [InlineData(".github/contributing.md")]
    [InlineData(".github/pull_request_template.md")]
    [InlineData("docs/PULL_REQUEST_TEMPLATE.md")]
    [InlineData(".github/PULL_REQUEST_TEMPLATE/release.md")]
    [InlineData(".github/ISSUE_TEMPLATE/bug_report.md")]
    [InlineData(".gitlab/merge_request_templates/Default.md")]
    [InlineData("DESIGN.md")]
    [InlineData("frontend/DESIGN.md")]
    public void Pool_document_is_allowed_and_not_measured(string path)
    {
        using var repository = Fixtures.Compliant()
            .WriteLines(path, 400)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains(path), run.Output);
    }

    [Theory]
    [InlineData("frontend/CONTRIBUTING.md")]
    [InlineData("docs/guides/SECURITY.md")]
    [InlineData("frontend/.github/pull_request_template.md")]
    [InlineData(".github/ISSUE_TEMPLATE/nested/bug.md")]
    [InlineData(".github/NOTES.md")]
    [InlineData("docs/DESIGN-notes.md")]
    public void Forge_name_outside_a_forge_location_is_still_unexpected(string path)
    {
        using var repository = Fixtures.Compliant()
            .WriteFile(path, "# Text\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains(path), run.Output);
        Assert.True(run.OutputContains("unexpected tracked Markdown"), run.Output);
    }

    [Fact]
    public void Explanation_names_the_pool()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("DESIGN.md"), run.Output);
        Assert.True(run.OutputContains("CONTRIBUTING.md"), run.Output);
        Assert.True(run.OutputContains(".github/ISSUE_TEMPLATE/"), run.Output);
    }

    [Fact]
    public void Upgrade_route_announces_the_pool_and_the_guide()
    {
        using var repository = Fixtures.WithRawFrame("""
            { "version": "3.3.0", "settings": {}, "policy": { "docs.policy": "required" } }
            """).Commit();

        var run = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Release 3.4 additions"), run.Output);
        Assert.True(run.OutputContains("harness guide"), run.Output);
    }
}
