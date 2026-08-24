using System.Globalization;
using System.Text.RegularExpressions;

namespace Harness.Tests;

/// <summary>The caller-visible contract: selection, explanation, timings and exit semantics.</summary>
public sealed class CliContractTests
{
    [Fact]
    public void Every_attempted_gate_reports_a_duration()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--verbose");

        var durations = Regex.Matches(run.Output, @"\(([0-9.]+) ms\)")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        Assert.NotEmpty(durations);
        Assert.All(durations, duration => Assert.True(duration >= 0, run.Output));

        // The executed gate and the Git evidence it needs both cost measurable time.
        Assert.True(durations.Sum() > 0, run.Output);
        Assert.True(run.OutputContains("git evidence"), run.Output);
    }

    [Fact]
    public void Repository_path_argument_is_checked_from_another_working_directory()
    {
        using var repository = Fixtures.Compliant();
        using var elsewhere = TemporaryDirectory.Create();

        var run = HarnessCli.Run(elsewhere.Path, "check", repository.Path, "--verbose");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("docs.policy"), run.Output);
    }

    [Fact]
    public void A_subdirectory_is_checked_against_its_repository_root()
    {
        using var repository = Fixtures.Compliant().WriteFile("src/keep.txt", "keep\n").Commit();

        var run = HarnessCli.Run(repository.Absolute("src"), "check");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Only_selects_the_named_check()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("✅ docs.policy"), run.Output);
    }

    [Fact]
    public void Only_accepts_a_group_identifier()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "docs");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("✅ docs.policy"), run.Output);
    }

    [Fact]
    public void Skipped_checks_stay_visible_in_the_summary()
    {
        using var repository = Fixtures.Compliant().WriteLines("AGENTS.md", 400).Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--skip", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("⏭️ docs.policy"), run.Output);
    }

    [Fact]
    public void Incomplete_checks_offer_a_verbose_details_command_in_compact_output()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Silent("tests.architecture"));

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("Details: harness check --only <check-id> --verbose"), run.Output);
    }

    [Fact]
    public void A_run_in_which_nothing_ran_does_not_claim_the_repository_passed()
    {
        using var repository = Fixtures.Compliant().WriteLines("AGENTS.md", 400).Commit();

        var run = HarnessCli.Run(
            repository.Path,
            "check",
            "--skip",
            "harness,docs,maintainability,duplication,frame");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("PASS"), run.Output);
        Assert.True(run.OutputContains("NOTHING VERIFIED"), run.Output);
    }

    [Fact]
    public void Help_documents_the_shipped_check_and_group_vocabulary()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "help");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("docs.policy"), run.Output);
        Assert.True(run.OutputContains("group docs"), run.Output);
        Assert.True(run.OutputContains("C# comment density limit"), run.Output);
    }

    [Fact]
    public void Every_shipped_check_identifier_can_be_explained_and_selected()
    {
        using var repository = Fixtures.Compliant();

        var identifiers = HarnessCli.ShippedCheckIds(repository.Path);

        Assert.NotEmpty(identifiers);
        foreach (var identifier in identifiers)
        {
            var explanation = HarnessCli.Run(repository.Path, "explain", identifier);
            Assert.Equal(0, explanation.ExitCode);
            Assert.True(explanation.OutputContains("Rationale"), explanation.Output);
            Assert.True(explanation.OutputContains("Remediation"), explanation.Output);

            var selected = HarnessCli.Run(repository.Path, "check", "--only", identifier);
            Assert.Equal(0, selected.ExitCode);
            Assert.True(selected.OutputContains(identifier), selected.Output);
        }
    }

    [Fact]
    public void Unknown_check_identifier_is_a_tool_error()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "docs.plicy");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("docs.plicy"), run.Output);
        Assert.True(run.OutputContains("docs.policy"), run.Output);
    }

    [Fact]
    public void A_directory_outside_a_git_repository_is_a_tool_error()
    {
        using var directory = TemporaryDirectory.Create();

        var run = HarnessCli.Run(directory.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("Git"), run.Output);
    }

    [Fact]
    public void Explain_describes_the_check_and_its_remediation()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Rationale"), run.Output);
        Assert.True(run.OutputContains("Remediation"), run.Output);
        Assert.True(run.OutputContains("AGENTS.md"), run.Output);
        Assert.True(run.OutputContains("150"), run.Output);
    }

    [Fact]
    public void Explaining_an_unknown_check_is_a_tool_error()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", "docs.plicy");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("docs.plicy"), run.Output);
    }

    [Fact]
    public void Default_output_stays_concise()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check");

        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= HarnessCli.ConciseLineBudget(repository.Path), run.Output);
    }

    [Fact]
    public void Check_lists_every_status_and_verbose_restores_the_evidence()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent())
            .WriteFile("src/App/Widget.cs", Fixtures.FormattedSource)
            .Commit();

        var concise = HarnessCli.Run(repository.Path, "check");
        var verbose = HarnessCli.Run(repository.Path, "check", "--verbose");

        Assert.True(concise.Output.StartsWith("PASS  ", StringComparison.Ordinal), concise.Output);
        Assert.All(
            HarnessCli.ShippedCheckIds(repository.Path),
            identifier => Assert.True(concise.OutputContains(identifier), concise.Output));
        Assert.Equal(
            HarnessCli.ShippedCheckIds(repository.Path).Count,
            concise.Output.Split('\n').Count(line => line.StartsWith('✅') || line.StartsWith('➖')));
        Assert.False(concise.OutputContains("git evidence"), concise.Output);
        Assert.True(verbose.OutputContains("passed"), verbose.Output);
        Assert.True(verbose.OutputContains("git evidence"), verbose.Output);
    }

    [Fact]
    public void A_failed_status_ends_with_its_finding_count_and_a_focused_verbose_command()
    {
        using var repository = Fixtures.Compliant()
            .WriteLines("AGENTS.md", 151)
            .WriteFile("docs/stale.md", "# Stale\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(lines, line => line.StartsWith("❌ docs.policy", StringComparison.Ordinal)
            && line.EndsWith('2'));
        Assert.Contains("Details: harness check --only <check-id> --verbose", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("Check ids: ", StringComparison.Ordinal));
        Assert.Equal(
            "harness check [path] [--only <ids>] [--skip <ids>] [--verbose]",
            lines[^1]);
        Assert.False(run.OutputContains("violation"), run.Output);
    }

    [Fact]
    public void A_single_check_can_be_run_with_verbose_findings()
    {
        using var repository = Fixtures.Compliant().WriteLines("AGENTS.md", 151).Commit();

        var run = HarnessCli.Run(
            repository.Path,
            "check",
            "--only",
            "docs.policy",
            "--verbose");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("❌ docs.policy"), run.Output);
        Assert.True(run.OutputContains("docs.policy"), run.Output);
        Assert.True(run.OutputContains("violation"), run.Output);
        Assert.DoesNotContain(
            run.Output.Split('\n'),
            line => line.StartsWith('✅')
                || line.StartsWith("⚠️", StringComparison.Ordinal)
                || line.StartsWith('➖')
                || line.StartsWith("⏭️", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unusable_command_line_is_a_tool_error()
    {
        using var repository = Fixtures.Compliant();

        Assert.Equal(2, HarnessCli.Run(repository.Path).ExitCode);
        Assert.Equal(2, HarnessCli.Run(repository.Path, "inspect").ExitCode);
        Assert.Equal(2, HarnessCli.Run(repository.Path, "check", "--only").ExitCode);
        Assert.Equal(2, HarnessCli.Run(repository.Path, "check", "--loud").ExitCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Checking_does_not_modify_tracked_content_or_git_state(bool compliant)
    {
        using var repository = compliant
            ? Fixtures.Compliant()
            : Fixtures.Compliant().WriteLines("AGENTS.md", 400).WriteFile("docs/stale.md", "# Stale\n").Commit();

        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(compliant ? 0 : 1, run.ExitCode);
        Assert.Equal(before, repository.TrackedState());
    }
}
