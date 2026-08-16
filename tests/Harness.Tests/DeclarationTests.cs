namespace Harness.Tests;

/// <summary>
/// The frame's questions. A declaration is a claim the repository makes; Git is what stops
/// the claim from being free. These tests fix the line between the two: what an answer may
/// establish on its own, and what Git is allowed to overrule.
/// </summary>
public sealed class DeclarationTests
{
    [Fact]
    public void An_address_that_git_tracks_is_a_proven_declaration()
    {
        using var repository = Fixtures.DotNet(
            Frame.Answering().At("tests.unit", "tests/App.Tests"),
            "tests/App.Tests/App.Tests.csproj",
            Fixtures.TestProject);

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("declared and proven"), run.Output);
        Assert.True(run.OutputContains("tests/App.Tests"), run.Output);
    }

    [Fact]
    public void A_directory_address_is_proven_by_anything_tracked_under_it()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().At("format", "config"))
            .WriteFile("config/nested/.editorconfig", "root = true\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.format");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("declared and proven"), run.Output);
    }

    [Fact]
    public void An_address_git_does_not_track_is_a_violation()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().At("tests.unit", "tests/Unit"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("tests/Unit"), run.Output);
        Assert.True(run.OutputContains("Git tracks nothing there"), run.Output);
    }

    [Fact]
    public void An_untracked_address_does_not_prove_a_declaration()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().At("format", ".editorconfig"))
            .WriteFile(".editorconfig", "root = true\n");

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.format");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains(".editorconfig"), run.Output);
    }

    [Fact]
    public void A_claim_without_an_address_is_a_readiness_gap_rather_than_a_pass()
    {
        using var repository = Fixtures.Compliant(
            Frame.Answering().Claiming("lint", "analyzers live in Directory.Build.props"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.lint");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("readiness gap"), run.Output);
        Assert.True(run.OutputContains("declared without an address"), run.Output);
        Assert.True(run.OutputContains("Directory.Build.props"), run.Output);
        Assert.False(run.OutputContains("PASS\n"), run.Output);
    }

    [Fact]
    public void A_claim_without_an_address_names_the_evidence_git_can_offer_as_one()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Claiming("format"))
            .WriteFile(".editorconfig", "root = true\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.format");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("could name as its address"), run.Output);
        Assert.True(run.OutputContains(".editorconfig"), run.Output);
    }

    [Fact]
    public void A_deliberate_absence_is_a_readiness_gap_and_not_a_violation()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.architecture");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("declared absent"), run.Output);
        Assert.True(run.OutputContains("fixture owns nothing here"), run.Output);
    }

    [Fact]
    public void An_unanswered_question_is_a_readiness_gap_that_says_how_to_answer_it()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().Silent("tests.unit"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("undeclared"), run.Output);
        Assert.True(run.OutputContains("declarations.tests.unit"), run.Output);
        Assert.True(run.OutputContains("applicable"), run.Output);
    }

    [Fact]
    public void A_question_declared_inapplicable_is_reported_as_such()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().NotApplicable("typecheck"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.typecheck");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void Git_refutes_a_denial_of_integration_tests()
    {
        using var repository = Fixtures.DotNet(
            Frame.Answering(),
            "tests/App.IntegrationTests/App.IntegrationTests.csproj",
            Fixtures.IntegrationTestProject);

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.integration");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("Microsoft.AspNetCore.Mvc.Testing"), run.Output);
        Assert.True(run.OutputContains("App.IntegrationTests.csproj"), run.Output);
    }

    [Fact]
    public void Git_refutes_a_denial_of_architecture_rules()
    {
        using var repository = Fixtures.DotNet(
            Frame.Answering(),
            "tests/App.Architecture/App.Architecture.csproj",
            Fixtures.ArchitectureTestProject);

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.architecture");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("NetArchTest"), run.Output);
    }

    [Fact]
    public void Git_refutes_a_denial_of_declared_web_machinery()
    {
        using var repository = Fixtures.Web(Frame.Answering(), Fixtures.WebManifest);

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("declares the script `lint`"), run.Output);
        Assert.True(run.OutputContains("declares the script `typecheck`"), run.Output);
    }

    [Fact]
    public void Git_refutes_a_question_declared_inapplicable_that_the_repository_plainly_has()
    {
        using var repository = Fixtures.Web(Frame.Answering().NotApplicable("typecheck"), Fixtures.WebManifest);

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.typecheck");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void Web_dependencies_refute_a_denial_of_integration_and_architecture_evidence()
    {
        using var repository = Fixtures.Web(
            Frame.Answering(),
            Fixtures.WebManifestWithIntegrationAndArchitectureEvidence);

        var integration = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.integration");
        var architecture = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.architecture");

        Assert.Equal(1, integration.ExitCode);
        Assert.True(integration.OutputContains("@playwright/test"), integration.Output);
        Assert.Equal(1, architecture.ExitCode);
        Assert.True(architecture.OutputContains("dependency-cruiser"), architecture.Output);
    }

    /// <summary>
    /// A test SDK marker cannot tell a unit test from an acceptance test, so it must not be
    /// allowed to call a repository a liar. This is the one question where evidence only
    /// ever hints.
    /// </summary>
    [Fact]
    public void A_test_project_does_not_refute_a_denial_of_unit_tests()
    {
        using var repository = Fixtures.DotNet(
            Frame.Answering(),
            "tests/App.Tests/App.Tests.csproj",
            Fixtures.TestProject);

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("declared absent"), run.Output);
    }

    [Fact]
    public void Evidence_the_harness_does_not_recognize_never_refutes_an_answer()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tests/steiger.config.ts", "export default [];\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void A_declaration_says_nothing_about_what_lives_at_its_address()
    {
        using var repository = Fixtures.Compliant(Frame.Answering().At("tests.unit", "README.md"));

        var run = HarnessCli.Run(repository.Path, "check", "--only", "declaration.tests.unit");

        // The address is real, so the declaration stands. The report refuses to imply more
        // than that, because the harness read nothing at the address.
        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not evidence of what lives at it"), run.Output);
    }
}
