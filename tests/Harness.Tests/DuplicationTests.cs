using System.Globalization;
using System.Text.RegularExpressions;

namespace Harness.Tests;

public sealed class DuplicationTests
{
    private const string Check = "duplication.csharp";

    private const int ShownFindingBudget = 6;

    private static int ReportedFindings(CliRun run)
        => run.Output.Split('\n').Count(line => line.Contains("advisory", StringComparison.Ordinal));

    private static void AssertBoundedOutput(CliRun run, string repositoryPath)
    {
        Assert.True(ReportedFindings(run) <= ShownFindingBudget, run.Output);

        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= HarnessCli.ConciseLineBudget(repositoryPath), run.Output);
    }

    [Fact]
    public void A_repository_without_csharp_sources_is_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
        Assert.False(run.OutputContains("passed"), run.Output);
    }

    [Fact]
    public void A_block_repeated_across_files_is_reported_at_every_location()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.Block("First", "seed", "first")),
            ("src/App/Second.cs", DuplicationSources.Block("Second", "start", "second")));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains(Check), run.Output);
        Assert.True(run.OutputContains("normalized lines"), run.Output);
        Assert.True(run.OutputContains("src/App/First.cs:1"), run.Output);
        Assert.True(run.OutputContains("src/App/Second.cs:1"), run.Output);
    }

    [Fact]
    public void Overlapping_windows_of_one_repetition_become_a_single_finding()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.Block("First", "seed", "first")),
            ("src/App/Second.cs", DuplicationSources.Block("Second", "start", "second")));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, Occurrences(run.Output, "normalized lines"));
    }

    [Fact]
    public void A_third_shorter_copy_never_makes_the_same_lines_be_reported_twice()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.Block("First", "seed", "first")),
            ("src/App/Second.cs", DuplicationSources.Block("Second", "start", "second")),
            ("src/App/Third.cs", DuplicationSources.TruncatedBlock("Third", "origin")));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("occurs 3 times"), run.Output);
        Assert.True(run.OutputContains("src/App/Third.cs:"), run.Output);
        AssertReportedRegionsDoNotOverlap(run.Output);
    }

    [Fact]
    public void A_repetition_confined_to_one_file_is_not_reported()
    {
        using var repository = Repository(("src/App/Twice.cs", DuplicationSources.SameBlockTwiceInOneFile));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("normalized lines"), run.Output);
    }

    [Fact]
    public void Code_quoted_inside_a_string_literal_is_not_matched_against_real_code()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.Block("First", "seed", "first")),
            ("src/App/Snippet.cs", DuplicationSources.BlockQuotedInARawString));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("normalized lines"), run.Output);
    }

    [Fact]
    public void Awkward_character_literals_do_not_desynchronize_the_comparison()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.AwkwardCharactersThenBlock("First", "seed", "first")),
            ("src/App/Second.cs", DuplicationSources.Block("Second", "start", "second")));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("src/App/First.cs:"), run.Output);
        Assert.True(run.OutputContains("src/App/Second.cs:"), run.Output);
        AssertReportedRegionsDoNotOverlap(run.Output);
    }

    [Fact]
    public void Unrelated_templates_of_the_same_shape_are_reported_only_as_a_lexical_match()
    {
        using var repository = Repository(
            ("src/App/Invoice.cs", DuplicationSources.PropertyBag("Invoice", "Supplier")),
            ("src/App/Patient.cs", DuplicationSources.PropertyBag("Patient", "Clinic")));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("lexically repeated"), run.Output);
        Assert.False(run.OutputContains("violation"), run.Output);
        Assert.False(run.OutputContains("duplicate behaviour"), run.Output);
    }

    [Fact]
    public void Short_unrelated_types_are_not_reported()
    {
        using var repository = Repository(
            ("src/App/Money.cs", DuplicationSources.SmallRecord("Money", "Amount", "Currency")),
            ("src/App/Point.cs", DuplicationSources.SmallRecord("Point", "Left", "Right")));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("normalized lines"), run.Output);
    }

    [Fact]
    public void Awkward_literals_do_not_hide_the_repetition_that_follows_them()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.AwkwardLiteralsThenBlock("First", "seed", "first")),
            ("src/App/Second.cs", DuplicationSources.Block("Second", "start", "second")));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("src/App/First.cs:"), run.Output);
        Assert.True(run.OutputContains("src/App/Second.cs:"), run.Output);
    }

    [Fact]
    public void Generated_and_build_output_sources_are_excluded()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/obj/Debug/First.cs", DuplicationSources.Block("First", "seed", "first"))
            .WriteFile("src/App/Second.g.cs", DuplicationSources.Block("Second", "start", "second"))
            .WriteFile("src/App/Third.generated.cs", DuplicationSources.Block("Third", "origin", "third"))
            .WriteFile("src/App/Fourth.Designer.cs", DuplicationSources.Block("Fourth", "base", "fourth"))
            .WriteFile("src/App/Marked.cs", "// <auto-generated />\n" + DuplicationSources.Block("Fifth", "root", "fifth"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void Duplication_findings_are_advisory_and_do_not_fail_the_run()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.Block("First", "seed", "first")),
            ("src/App/Second.cs", DuplicationSources.Block("Second", "start", "second")));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("advisory"), run.Output);
    }

    [Fact]
    public void The_report_does_not_claim_proven_duplicate_behaviour()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.Block("First", "seed", "first")),
            ("src/App/Second.cs", DuplicationSources.Block("Second", "start", "second")));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("lexical"), run.Output);
    }

    [Fact]
    public void Many_occurrences_of_one_block_stay_bounded_and_report_how_many_there_are()
    {
        var repository = Fixtures.Compliant();
        for (var index = 0; index < 24; index++)
        {
            repository.WriteFile($"src/App/Copy{index:00}.cs", DuplicationSources.Block($"Copy{index:00}", "seed", "copy"));
        }

        using var committed = repository.Commit();

        var run = HarnessCli.RunVerbose(committed.Path, "check", "--only", Check);

        AssertBoundedOutput(run, committed.Path);
        Assert.True(run.OutputContains("24"), run.Output);
    }

    [Fact]
    public void Many_repeated_blocks_stay_bounded_and_report_the_rest_as_a_count()
    {
        var repository = Fixtures.Compliant();
        for (var index = 0; index < 8; index++)
        {
            repository
                .WriteFile($"src/App/Left{index}.cs", DuplicationSources.DistinctBlock(index, "Left"))
                .WriteFile($"src/App/Right{index}.cs", DuplicationSources.DistinctBlock(index, "Right"));
        }

        using var committed = repository.Commit();

        var run = HarnessCli.RunVerbose(committed.Path, "check", "--only", Check);

        AssertBoundedOutput(run, committed.Path);
        Assert.True(run.OutputContains("8 repeated blocks"), run.Output);
    }

    [Fact]
    public void The_gate_reports_a_duration_and_leaves_the_repository_unchanged()
    {
        using var repository = Repository(
            ("src/App/First.cs", DuplicationSources.Block("First", "seed", "first")),
            ("src/App/Second.cs", DuplicationSources.Block("Second", "start", "second")));
        var before = repository.TrackedState();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains(" ms)"), run.Output);
        Assert.Equal(before, repository.TrackedState());
    }

    [Fact]
    public void Explain_states_the_normalization_the_limits_and_when_not_to_refactor()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Normalization"), run.Output);
        Assert.True(run.OutputContains("Limits"), run.Output);
        Assert.True(run.OutputContains("False positives"), run.Output);
        Assert.True(run.OutputContains("judgement"), run.Output);
    }

    [Fact]
    public void Explain_refuses_the_claim_the_comparison_does_not_support()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.True(run.OutputContains("not proven duplicate behaviour"), run.Output);
    }

    private static void AssertReportedRegionsDoNotOverlap(string output)
    {
        var regions = Regex.Matches(output, @"([\w./-]+\.cs):(\d+)-(\d+)")
            .Select(match => (
                Path: match.Groups[1].Value,
                First: int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                Last: int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
            .Distinct()
            .ToList();

        Assert.NotEmpty(regions);
        foreach (var file in regions.GroupBy(region => region.Path))
        {
            var ordered = file.OrderBy(region => region.First).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                Assert.True(
                    ordered[index].First > ordered[index - 1].Last,
                    $"{ordered[index - 1]} and {ordered[index]} overlap in:\n{output}");
            }
        }
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static RepositoryFixture Repository(params (string Path, string Source)[] sources)
    {
        var repository = Fixtures.Compliant();
        foreach (var (path, source) in sources)
        {
            repository.WriteFile(path, source);
        }

        return repository.Commit();
    }
}
