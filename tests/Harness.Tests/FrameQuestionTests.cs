namespace Harness.Tests;

/// <summary>The repository answers frame questions; the harness reports and never fact-checks them.</summary>
public sealed class FrameQuestionTests
{
    [Fact]
    public void A_located_answer_passes_without_inspecting_or_tracking_its_paths()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Located("tests.unit", "tests/Unit"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.tests.unit");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("repository answers present"), run.Output);
        Assert.True(run.OutputContains("tests/Unit"), run.Output);
        Assert.True(run.OutputContains("does not inspect"), run.Output);
    }

    [Fact]
    public void A_positive_answer_without_an_address_is_complete()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Present("lint", "analyzers are enabled in project files"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.lint");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("passed"), run.Output);
        Assert.True(run.OutputContains("analyzers are enabled"), run.Output);
        Assert.True(run.OutputContains("self-reported"), run.Output);
    }

    [Fact]
    public void Advisory_policy_keeps_a_deliberate_absence_as_a_visible_readiness_gap()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Policy("frame.tests.architecture", "advisory"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.tests.architecture");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("readiness gap"), run.Output);
        Assert.True(run.OutputContains("repository answers absent"), run.Output);
        Assert.True(run.OutputContains("fixture owns nothing here"), run.Output);
    }

    [Fact]
    public void An_absent_answer_is_a_violation_when_policy_is_required()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering());

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.tests.unit");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("explicit required policy"), run.Output);
    }

    [Fact]
    public void A_question_answered_inapplicable_is_reported_as_such()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().NotApplicable("typecheck"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.typecheck");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
        Assert.True(run.OutputContains("no stack for it"), run.Output);
    }

    [Fact]
    public void Repository_contents_never_refute_its_answers()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile(
                "tests/App.IntegrationTests/App.IntegrationTests.csproj",
                Fixtures.PreviouslyRecognizedIntegrationProject)
            .WriteFile("package.json", Fixtures.PreviouslyRecognizedWebManifest)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("violation"), run.Output);
        Assert.False(run.OutputContains("Git refutes"), run.Output);
    }

    [Fact]
    public void Explain_states_that_an_answer_is_not_fact_checked()
    {
        var run = HarnessCli.Run(Directory.GetCurrentDirectory(), "explain", "frame.build");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("self-reported"), run.Output);
        Assert.True(run.OutputContains("not inspected"), run.Output);
    }

    [Fact]
    public void Verify_names_one_repository_owned_entry_point_without_running_it()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Located("verify", "verify.sh"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.verify");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("verify.sh"), run.Output);
        Assert.True(run.OutputContains("does not inspect"), run.Output);

        var explanation = HarnessCli.Run(repository.Path, "explain", "frame.verify");
        Assert.Equal(0, explanation.ExitCode);
        Assert.True(explanation.OutputContains("every applicable quality check"), explanation.Output);
        Assert.True(explanation.OutputContains("never executes"), explanation.Output);
    }

    [Theory]
    [InlineData("{ \"present\": true, \"reason\": \"CI knows how\" }", "must use `paths`")]
    [InlineData("{ \"applicable\": false, \"reason\": \"no CI\" }", "cannot be not applicable")]
    public void Verify_requires_a_runnable_address_for_every_repository(string answer, string expected)
    {
        using var repository = Fixtures.Compliant(Frame.Answering().With("verify", answer));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame.verify");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(expected), run.Output);
    }
}
