namespace Harness.Tests;

/// <summary>
/// The frame file itself: when it may be read, when it may not, and what a repository can
/// say through it about strictness and about findings it has consciously accepted.
/// </summary>
public sealed class HarnessFrameTests
{
    [Fact]
    public void A_repository_without_a_frame_cannot_be_verified()
    {
        using var repository = Fixtures.WithoutAFrame();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("INCOMPLETE"), run.Output);
        Assert.True(run.OutputContains(".harness.json"), run.Output);
    }

    [Fact]
    public void A_repository_without_a_frame_is_shown_the_frame_it_needs()
    {
        using var repository = Fixtures.WithoutAFrame();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.True(run.OutputContains("\"declarations\""), run.Output);
        Assert.True(run.OutputContains("tests.unit"), run.Output);
    }

    [Fact]
    public void An_untracked_frame_does_not_count()
    {
        using var repository = Fixtures.WithoutAFrame()
            .WriteFile(".harness.json", Frame.Answering().ToString());

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("is not tracked"), run.Output);
    }

    [Theory]
    [InlineData("{ not json", "not readable as JSON")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("""{ "version": 2 }""", "'version' must be 1")]
    [InlineData("""{ "declarations": [] }""", "'declarations' must be an object")]
    [InlineData("""{ "checks": {} }""", "not a key this harness reads")]
    [InlineData("""{ "declarations": { "tests": {} } }""", "not a question this harness asks")]
    [InlineData("""{ "declarations": { "format": { "present": true } } }""", "needs a non-empty 'reason'")]
    [InlineData("""{ "declarations": { "format": { "paths": [] } } }""", "is empty")]
    [InlineData("""{ "declarations": { "format": { "paths": [".x"], "present": true } } }""", "already an answer")]
    [InlineData("""{ "declarations": { "format": { "applicable": false } } }""", "why the question does not apply")]
    [InlineData("""{ "declarations": { "format": { "hint": 1 } } }""", "not a key this harness reads")]
    [InlineData("""{ "policy": { "docs.plicy": "off" } }""", "not a check or group this harness ships")]
    [InlineData("""{ "policy": { "docs.policy": "lenient" } }""", "must be required, advisory or off")]
    [InlineData("""{ "suppress": {} }""", "'suppress' must be an array")]
    [InlineData("""{ "suppress": [{ "check": "docs.policy", "location": "a.md" }] }""", "must say why")]
    [InlineData("""{ "suppress": [{ "check": "nope", "location": "a", "reason": "b" }] }""", "must name a check")]
    public void An_unsound_frame_ends_the_run_as_incomplete(string frame, string explanation)
    {
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
    }

    [Fact]
    public void An_unsound_frame_leaves_no_question_answered()
    {
        using var repository = Fixtures.WithRawFrame("{ not json");

        var run = HarnessCli.Run(repository.Path, "check");

        // Every declaration check depends on the frame, so none of them may report a state
        // that reads as an answer.
        Assert.Equal(2, run.ExitCode);
        Assert.False(run.OutputContains("declared and proven"), run.Output);
        Assert.False(run.OutputContains("declared absent"), run.Output);
        Assert.False(run.OutputContains("readiness gap"), run.Output);
    }

    [Fact]
    public void A_frame_that_omits_every_optional_section_is_valid()
    {
        using var repository = Fixtures.WithRawFrame("{}\n");

        var run = HarnessCli.Run(repository.Path, "check", "--only", "harness.config");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("0 declarations"), run.Output);
    }

    [Fact]
    public void Required_policy_turns_a_readiness_gap_into_a_violation()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Policy("declaration.tests.unit", "required"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("sets this check to required"), run.Output);
    }

    [Fact]
    public void Required_policy_is_satisfied_by_a_proven_address()
    {
        using var repository = Fixtures.DotNet(
            Frame.Answering()
                .At("tests.unit", "tests/App.Tests")
                .Policy("declaration.tests.unit", "required"),
            "tests/App.Tests/App.Tests.csproj",
            Fixtures.TestProject);

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Required_policy_accepts_a_group_identifier()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Policy("declaration", "required"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration");

        Assert.Equal(1, run.ExitCode);
    }

    [Fact]
    public void A_check_identifier_outranks_its_group_in_policy()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering()
                .Policy("declaration", "required")
                .Policy("declaration.tests.unit", "off"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("turns this check off"), run.Output);
    }

    [Fact]
    public void Advisory_policy_reports_a_violation_without_failing_the_run()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering()
                .At("tests.unit", "tests/Unit")
                .Policy("declaration.tests.unit", "advisory"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("advisory"), run.Output);
        Assert.True(run.OutputContains("tests/Unit"), run.Output);
    }

    [Fact]
    public void A_check_turned_off_does_not_run_but_stays_visible()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Policy("docs.policy", "off"))
            .WriteLines("ROOT.md", 400)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("skipped"), run.Output);
        Assert.True(run.OutputContains("docs.policy"), run.Output);
    }

    [Fact]
    public void A_named_exception_clears_a_violation_and_stays_on_the_report()
    {
        using var repository = Fixtures
            .Compliant(Frame.Answering().Suppressing("docs.policy", "ROOT.md", "split in IDP-142"))
            .WriteLines("ROOT.md", 400)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("suppressed"), run.Output);
        Assert.True(run.OutputContains("split in IDP-142"), run.Output);
        Assert.True(run.OutputContains("named exception"), run.Output);
    }

    [Fact]
    public void An_exception_covers_what_is_under_the_directory_it_names()
    {
        using var repository = Fixtures.Compliant(Frame.Answering()
            .At("tests.unit", "tests/Unit")
            .Suppressing("declaration", "tests", "legacy layout"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("suppressed"), run.Output);
    }

    [Fact]
    public void An_exception_does_not_reach_a_finding_of_another_check()
    {
        using var repository = Fixtures
            .Compliant(Frame.Answering().Suppressing("declaration.format", "ROOT.md", "wrong check on purpose"))
            .WriteLines("ROOT.md", 400)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
    }

    [Fact]
    public void An_exception_that_matched_nothing_is_reported_on_the_frame()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Suppressing("docs.policy", "gone.md", "was fixed last quarter"));

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("matched nothing in this run"), run.Output);
        Assert.True(run.OutputContains("was fixed last quarter"), run.Output);
    }

    [Fact]
    public void Reading_the_frame_does_not_modify_the_repository()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Policy("declaration", "required").Suppressing("docs.policy", "ROOT.md", "why not"));

        var before = repository.TrackedState();

        HarnessCli.Run(repository.Path, "check");

        Assert.Equal(before, repository.TrackedState());
    }
}
