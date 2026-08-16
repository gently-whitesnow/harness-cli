namespace Harness.Tests;

/// <summary>The repository answers frame questions; the harness reports and never fact-checks them.</summary>
public sealed class FrameQuestionTests
{
    [Fact]
    public void A_located_answer_passes_without_inspecting_or_tracking_its_paths()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Located("tests.unit", "tests/Unit"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "frame.tests.unit");

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

        var run = HarnessCli.Run(repository.Path, "check", "--only", "frame.lint");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("passed"), run.Output);
        Assert.True(run.OutputContains("analyzers are enabled"), run.Output);
        Assert.True(run.OutputContains("self-reported"), run.Output);
    }

    [Fact]
    public void A_deliberate_absence_is_a_visible_readiness_gap()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "frame.tests.architecture");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("readiness gap"), run.Output);
        Assert.True(run.OutputContains("repository answers absent"), run.Output);
        Assert.True(run.OutputContains("fixture owns nothing here"), run.Output);
    }

    [Fact]
    public void Required_policy_turns_an_absent_answer_into_a_violation()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Policy("frame.tests.unit", "required"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "frame.tests.unit");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("sets this check to required"), run.Output);
    }

    [Fact]
    public void A_question_answered_inapplicable_is_reported_as_such()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().NotApplicable("typecheck"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "frame.typecheck");

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

        var run = HarnessCli.Run(repository.Path, "check", "--only", "frame");

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
}
