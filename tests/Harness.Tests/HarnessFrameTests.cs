namespace Harness.Tests;

/// <summary>The frame file: complete answers, policy and named exceptions.</summary>
public sealed class HarnessFrameTests
{
    [Fact]
    public void A_repository_without_a_frame_cannot_be_verified()
    {
        using var repository = Fixtures.WithoutAFrame();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("INCOMPLETE"), run.Output);
        Assert.True(run.OutputContains(".harness.json"), run.Output);
        Assert.True(run.OutputContains("\"answers\""), run.Output);
    }

    [Fact]
    public void An_untracked_frame_does_not_count()
    {
        using var repository = Fixtures.WithoutAFrame()
            .WriteFile(".harness.json", Frame.Answering().ToString());

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("is not tracked"), run.Output);
    }

    [Theory]
    [InlineData("{ not json", "not readable as JSON")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("{}", "'version' must be a harness release")]
    [InlineData("""{ "version": 1 }""", "'version' must be a harness release")]
    [InlineData("""{ "version": "1.0" }""", "'version' must be a harness release")]
    [InlineData("""{ "version": "LATEST" }""", "'version' must be a harness release")]
    [InlineData("""{ "version": 1.5 }""", "'version' must be a harness release")]
    [InlineData("""{ "version": "0.9.0" }""", "no longer reproduces")]
    [InlineData("""{ "version": "99.0.0" }""", "newer than this binary")]
    [InlineData("""{ "version": "latest", "answers": [] }""", "'answers' must be an object")]
    [InlineData("""{ "version": "latest", "checks": {} }""", "not a key this harness reads")]
    [InlineData("""{ "version": "latest", "answers": { "tests": {} } }""", "not a question this harness asks")]
    public void An_unsound_frame_ends_the_run_as_incomplete(string frame, string explanation)
    {
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
    }

    [Theory]
    [InlineData("docs.plicy", "off", "not a check or group this harness ships")]
    [InlineData("docs.policy", "lenient", "must be required, advisory or off")]
    public void Invalid_policy_ends_the_run_as_incomplete(string selector, string value, string explanation)
    {
        using var repository = Fixtures.WithRawFrame(Frame.Answering().Policy(selector, value).ToString());

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
    }

    [Theory]
    [InlineData("typescript", """{ "applicable": false, "reason": "not used" }""", "not an applicability")]
    [InlineData("csharp", """{ "applicable": true, "reason": "used" }""", "must be false")]
    [InlineData("csharp", """{ "applicable": false }""", "must say why")]
    public void Invalid_applicability_ends_the_run_as_incomplete(
        string key,
        string value,
        string explanation)
    {
        var frame = Frame.AllPresent().ToString();
        frame = frame[..^2] + $",\n  \"applicability\": {{ \"{key}\": {value} }}\n}}\n";
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
    }

    [Theory]
    [InlineData("[]", "'settings' must be an object")]
    [InlineData("{ \"comments\": {} }", "is not configurable")]
    [InlineData("{ \"comments.csharp\": { \"percent\": 25 } }", "is not a setting")]
    [InlineData("{ \"comments.csharp\": { \"percentageLimit\": 101 } }", "must not exceed 100")]
    [InlineData("{ \"maintainability.csharp\": { \"methodLines\": -1 } }", "non-negative integer")]
    [InlineData("{ \"commits\": { \"language\": \"de\" } }", "must be 'en' or 'ru'")]
    [InlineData("{ \"commits\": { \"requireSetup\": \"yes\" } }", "must be true or false")]
    public void Invalid_settings_end_the_run_as_incomplete(string settings, string explanation)
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Settings(settings));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
    }

    [Theory]
    [InlineData("docs.policy", "AGENTS.md", "", "must say why")]
    [InlineData("nope", "AGENTS.md", "because", "must name a check")]
    public void Invalid_suppression_ends_the_run_as_incomplete(
        string check,
        string location,
        string reason,
        string explanation)
    {
        using var repository = Fixtures.WithRawFrame(
            Frame.Answering().Suppressing(check, location, reason).ToString());

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
    }

    [Fact]
    public void A_missing_answer_is_local_to_its_question()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Silent("tests.unit"));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("answers.tests.unit"), run.Output);
        Assert.True(run.OutputContains("harness.config"), run.Output);
        Assert.True(run.OutputContains("outcome: passed"), run.Output);
        Assert.True(run.OutputContains("repository answers absent"), run.Output);
        Assert.True(run.OutputContains("owner's intent is unclear"), run.Output);
        Assert.Equal(1, run.Output.Split("outcome: incomplete", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("""{ "present": true }""", "needs a non-empty 'reason'")]
    [InlineData("""{ "paths": [] }""", "is empty")]
    [InlineData("""{ "paths": [".x"], "present": true }""", "already an answer")]
    [InlineData("""{ "applicable": false }""", "why the question does not apply")]
    [InlineData("""{ "hint": 1 }""", "not a key this harness reads")]
    public void A_malformed_answer_is_local_to_its_question(string answer, string explanation)
    {
        using var repository = Fixtures.Compliant(Frame.Answering().With("format", answer));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
        Assert.True(run.OutputContains("harness.config"), run.Output);
        Assert.True(run.OutputContains("repository answers absent"), run.Output);
        Assert.True(run.OutputContains("\"format\": { \"paths\":"), run.Output);
        Assert.True(run.OutputContains("Do not invent a positive answer"), run.Output);
        Assert.Equal(1, run.Output.Split("outcome: incomplete", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Latest_uses_the_current_question_set()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Version("latest").Policy("frame", "advisory"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("repository answers absent"), run.Output);
    }

    [Fact]
    public void Required_policy_accepts_a_positive_self_report()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering()
                .Present("tests.unit", "the suite is generated by the build")
                .Policy("frame.tests.unit", "required"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.tests.unit");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void A_check_identifier_outranks_its_group_in_policy()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering()
                .Policy("frame", "required")
                .Policy("frame.tests.unit", "off"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.tests.unit");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("turns this check off"), run.Output);
    }

    [Fact]
    public void A_check_turned_off_does_not_run_but_stays_visible()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Policy("docs.policy", "off"))
            .WriteLines("AGENTS.md", 400)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("skipped"), run.Output);
        Assert.True(run.OutputContains("docs.policy"), run.Output);
    }

    [Fact]
    public void A_named_exception_clears_a_violation_and_stays_on_the_report()
    {
        using var repository = Fixtures
            .Compliant(Frame.AllPresent().Suppressing("docs.policy", "AGENTS.md", "split in HARNESS-142"))
            .WriteLines("AGENTS.md", 400)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("suppressed"), run.Output);
        Assert.True(run.OutputContains("split in HARNESS-142"), run.Output);
    }

    [Fact]
    public void An_exception_that_matched_nothing_is_reported_on_the_frame()
    {
        using var repository = Fixtures.Compliant(
            Frame.AllPresent().Suppressing("docs.policy", "gone.md", "was fixed last quarter"));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("matched nothing in this run"), run.Output);
        Assert.True(run.OutputContains("was fixed last quarter"), run.Output);
    }

    [Fact]
    public void Reading_the_frame_does_not_modify_the_repository()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Policy("frame", "required").Suppressing("docs.policy", "AGENTS.md", "why not"));

        var before = repository.TrackedState();

        HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(before, repository.TrackedState());
    }
}
