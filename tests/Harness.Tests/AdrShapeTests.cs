namespace Harness.Tests;

public sealed class AdrShapeTests
{
    private const string Valid = """
        # ADR-0001: First decision

        ## Status

        Date: 2026-09-24

        Accepted.

        ## Context

        A choice is needed.

        ## Decision

        Choose the simple form.

        ## Consequences

        The choice is recorded.
        """;

    [Fact]
    public void Valid_decision_passes_and_explanation_names_limits()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("adrs/0001-first-decision.md", Valid)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "adrs.shape");
        var explanation = HarnessCli.Run(repository.Path, "explain", "adrs.shape");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(0, explanation.ExitCode);
        Assert.Contains("wordLimit", explanation.Output);
        Assert.Contains("fencedLineLimit", explanation.Output);
    }

    [Theory]
    [InlineData("adrs/1-bad-name.md", Valid, "name must be")]
    [InlineData("adrs/0001-first-decision.md", "# ADR-0001\n\n## Status\n\nDate: 2026-09-24\n\nUnknown.\n\n## Context\n\nA\n\n## Decision\n\nB\n\n## Consequences\n\nC\n", "recognized status")]
    [InlineData("adrs/0001-first-decision.md", "# ADR-0001\n\n## Status\n\nAccepted.\n\n## Context\n\nA\n\n## Decision\n\nB\n\n## Consequences\n\nC\n", "header needs a date")]
    [InlineData("adrs/0001-first-decision.md", "# ADR-0001\n\n## Status\n\nDate: 2026-02-31\n\nAccepted.\n\n## Context\n\nA\n\n## Decision\n\nB\n\n## Consequences\n\nC\n", "header needs a date")]
    [InlineData("adrs/0001-first-decision.md", "# ADR-0001\n\n## Status\n\nDate: 2026-09-24\n\nAccepted.\n\n## Context\n\nA\n\n## Decision\n\nB\n\n## Consequences\n\nC\n\n## Appendix\n\nD\n", "unexpected H2 section")]
    [InlineData("adrs/0001-first-decision.md", "# ADR-0001\n\n## Status\n\nDate: 2026-09-24\n\nAccepted.\n\n## Context\n\nA\n\n## Decision\n\nB\n", "missing H2 section Consequences")]
    [InlineData("adrs/0001-first-decision.md", "# ADR-0001\n\n## Status\n\nDate: 2026-09-24\n\nSuperseded by ADR-9.\n\n## Context\n\nA\n\n## Decision\n\nB\n\n## Consequences\n\nC\n", "references missing ADR-0009")]
    public void Invalid_shape_reports_specific_finding(string path, string body, string finding)
    {
        using var repository = Fixtures.Compliant().WriteFile(path, body).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "adrs.shape");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(finding, run.Output);
    }

    [Fact]
    public void Duplicate_and_gap_are_reported()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("adrs/0001-first-decision.md", Valid)
            .WriteFile("adrs/0001-other-decision.md", Valid)
            .WriteFile("adrs/0003-third-decision.md", Valid)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "adrs.shape");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("duplicates", run.Output);
        Assert.Contains("ADR-0002 is missing", run.Output);
    }

    [Fact]
    public void Nested_markdown_does_not_escape_the_catalogue()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("adrs/archive/long-spec.md", "# Old specification\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "adrs.shape");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("must live directly under adrs/", run.Output);
    }

    [Fact]
    public void Upgrade_describes_the_required_section_and_limits()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version("3.6.0"));

        var run = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Release 3.7 additions", run.Output);
        Assert.Contains("wordLimit 1000", run.Output);
    }

    [Fact]
    public void Limits_are_read_from_frame_and_never_suggested_as_remediation()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Settings("""
            { "adrs.shape": { "wordLimit": 20, "fencedLineLimit": 1, "tableRowLimit": 1 } }
            """))
            .WriteFile("adrs/0001-first-decision.md", Valid + "\n```cs\nfirst\nsecond\n```\n\n| A |\n| --- |\n| one |\n| two |\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "adrs.shape");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("words exceeds wordLimit 20", run.Output);
        Assert.Contains("fenced lines exceeds fencedLineLimit 1", run.Output);
        Assert.Contains("above tableRowLimit 1", run.Output);
        Assert.DoesNotContain("raise", run.Output, StringComparison.OrdinalIgnoreCase);
    }
}
