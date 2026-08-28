namespace Harness.Tests;

/// <summary>The frame file: complete answers and policy.</summary>
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
    [InlineData("""{ "version": "0.9.0" }""", "upgrade required")]
    [InlineData("""{ "version": "99.0.0" }""", "upgrade required")]
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

    [Fact]
    public void A_focused_check_cannot_bypass_an_unsound_frame()
    {
        using var repository = Fixtures.WithRawFrame("{}");

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("harness.config"), run.Output);
        Assert.True(run.OutputContains("'version' must be a harness release"), run.Output);
        Assert.False(run.OutputContains("docs.policy"), run.Output);
    }

    [Theory]
    [InlineData("docs.plicy", "off", "not a check this harness ships")]
    [InlineData("maintainability.csharp", "off", "removed in harness 2.0")]
    [InlineData("cohesion.csharp", "advisory", "removed in harness 2.0")]
    [InlineData("docs.policy", "lenient", "must be required, advisory or off")]
    public void Invalid_policy_ends_the_run_as_incomplete(string selector, string value, string explanation)
    {
        using var repository = Fixtures.WithRawFrame(Frame.Answering().Policy(selector, value).ToString());

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
    }

    [Fact]
    public void Every_shipped_check_requires_an_explicit_policy_entry()
    {
        var frame = Frame.AllPresent().ToString().Replace(
            "    \"docs.policy\": \"required\",\n",
            string.Empty,
            StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("'policy' is missing explicit checks: docs.policy", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_applicability_axis_requires_an_explicit_entry()
    {
        var frame = Frame.AllPresent().ToString().Replace(
            ",\n    \"dotnet\": { \"applicable\": true }",
            string.Empty,
            StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("'applicability' is missing explicit entries: dotnet", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("typescript", """{ "applicable": false, "reason": "not used" }""", "not an applicability")]
    [InlineData("csharp", """{ "applicable": true, "reason": "used" }""", "reason' is only valid")]
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
    [InlineData("{ \"maintainability.csharp\": { \"methodLines\": 60 } }", "removed in harness 2.0")]
    [InlineData("{ \"cohesion.csharp\": { \"groups\": 2 } }", "removed in harness 2.0")]
    [InlineData("{ \"dependencies.csharp\": { \"incomingReferences\": 20 } }", "not part of the current contract")]
    [InlineData("{ \"duplication.csharp\": { \"windowLines\": 0 } }", "positive integer")]
    [InlineData("{ \"duplication.csharp\": { \"minimumTokens\": -1 } }", "non-negative integer")]
    [InlineData("{ \"commits\": { \"language\": \"de\" } }", "must be 'en' or 'ru'")]
    [InlineData("{ \"commits\": { \"requireSetup\": \"yes\" } }", "must be true or false")]
    public void Invalid_settings_end_the_run_as_incomplete(string settings, string explanation)
    {
        var frame = settings == "[]"
            ? Frame.Answering().RawSettings(settings)
            : Frame.Answering().Settings(settings);
        using var repository = Fixtures.Compliant(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(explanation), run.Output);
    }

    [Fact]
    public void Every_setting_requires_an_explicit_value()
    {
        var frame = Frame.AllPresent().Settings(
            """{ "duplication.csharp": { "minimumTokens": 91 } }""").ToString().Replace(
                ",\"minimumTokens\":91",
                string.Empty,
                StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("settings.duplication.csharp", run.Output, StringComparison.Ordinal);
        Assert.Contains("must be present", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("suppress")]
    [InlineData("overrides")]
    public void Removed_top_level_sections_end_the_run_as_incomplete(string section)
    {
        using var repository = Fixtures.WithRawFrame(
            $$"""{ "version": "latest", "{{section}}": {} }""");

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains($"'{section}' was removed in harness 2.0"), run.Output);
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
            Frame.Answering().Version("latest").FramePolicy("advisory"));

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
    public void Policy_is_explicit_for_one_check_identifier()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering()
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
    public void Reading_the_frame_does_not_modify_the_repository()
    {
        using var repository = Fixtures.Compliant(Frame.Answering());

        var before = repository.TrackedState();

        HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(before, repository.TrackedState());
    }
}
