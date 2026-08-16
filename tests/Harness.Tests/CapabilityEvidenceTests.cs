using System.Runtime.Versioning;

namespace Harness.Tests;

/// <summary>
/// What the harness may say about the quality capabilities a repository owns. The
/// vocabulary is the contract: detected, executed, not detected, unknown and not
/// applicable are five different statements, and only one of them is a claim about the
/// repository having something. None of them is a claim about the repository lacking it,
/// and none of them fails a run in v0.
/// </summary>
public sealed class CapabilityEvidenceTests
{
    /// <summary>
    /// The verdict a report opens a capability's evidence line with. Matching the whole word
    /// at the start of the line keeps `not detected` from reading as `detected`.
    /// </summary>
    private static bool Says(CliRun run, string word)
        => run.Output
            .Split('\n')
            .Any(line => line.Trim().StartsWith(word + " —", StringComparison.Ordinal));

    [Fact]
    public void A_repository_without_a_supported_stack_reports_capabilities_as_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("capability.tests"), run.Output);
        Assert.True(run.OutputContains("capability.integration"), run.Output);
        Assert.True(run.OutputContains("capability.architecture"), run.Output);
        Assert.True(run.OutputContains("not applicable"), run.Output);

        // A stack the repository does not have is not an absent capability.
        Assert.False(Says(run, "not detected"), run.Output);
    }

    /// <summary>
    /// Discovered test projects are evidence in their own right. Nothing needs to run for
    /// the harness to report that the repository owns tests.
    /// </summary>
    [Fact]
    public void A_tracked_test_project_is_detected_evidence()
    {
        using var repository = Fixtures.DotNetWithPassingTests();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.tests");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "detected"), run.Output);
        Assert.False(Says(run, "executed"), run.Output);
        Assert.True(run.OutputContains("tests/App.Tests/App.Tests.csproj"), run.Output);
    }

    /// <summary>
    /// A command that ran and passed is stronger evidence than a file that exists — but it
    /// still says nothing about what those tests assert.
    /// </summary>
    [Fact]
    public void A_test_command_that_ran_raises_the_evidence_to_executed()
    {
        using var repository = Fixtures.DotNetWithPassingTests();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.test,capability.tests");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "executed"), run.Output);
        Assert.True(run.OutputContains("dotnet.test"), run.Output);

        // Execution is not certification of what the tests cover.
        Assert.True(run.OutputContains("not evidence that they are complete"), run.Output);
    }

    /// <summary>
    /// The evidence a gate ran is the only evidence its exit status speaks for. `web.test`
    /// runs the repository's test script, so a passing run says nothing about the end-to-end
    /// suite or the boundary linter that live next to it.
    /// </summary>
    [Fact]
    public void A_passing_test_gate_does_not_claim_to_have_run_a_command_it_never_ran()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithIntegrationAndArchitectureEvidence)
            .Commit();

        // The declared dependencies are only there to be recognized; the gate needs them to
        // look installed before it will run the repository's own test script at all.
        Directory.CreateDirectory(repository.Absolute("node_modules"));

        foreach (var capability in new[] { "capability.integration", "capability.architecture" })
        {
            var run = HarnessCli.Run(repository.Path, "check", "--only", "web.test," + capability);

            Assert.Equal(0, run.ExitCode);
            Assert.True(run.OutputContains("npm run test"), run.Output);
            Assert.True(Says(run, "detected"), run.Output);
            Assert.False(Says(run, "executed"), run.Output);
        }
    }

    [Fact]
    public void A_web_test_script_the_gate_ran_is_executed_evidence()
    {
        using var repository = Fixtures.WebApplication();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.test,capability.tests");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "executed"), run.Output);
        Assert.True(run.OutputContains("web.test"), run.Output);
    }

    /// <summary>
    /// A .NET architecture library outside a test project is not run by `dotnet test`, so
    /// the report must not borrow that command's exit status for it.
    /// </summary>
    [Fact]
    public void Architecture_evidence_outside_a_test_project_is_not_executed()
    {
        using var repository = Fixtures.DotNetWithPassingTests()
            .WriteFile("src/Rules/Rules.csproj", Fixtures.LibraryReferencingArchitectureRules)
            .WriteFile("src/Rules/Layers.cs", "namespace Rules;\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.test,capability.architecture");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "detected"), run.Output);
        Assert.False(Says(run, "executed"), run.Output);
    }

    /// <summary>
    /// Evidence already found is not erased by a project that could not be read afterwards.
    /// The unreadable project is still reported, because it is the part of the answer the
    /// harness does not have.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("linux")]
    public void An_unreadable_project_does_not_erase_evidence_that_was_already_found()
    {
        using var repository = Fixtures.DotNetWithPassingTests();
        Unreadable(repository, "src/App/App.csproj");

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.tests");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "detected"), run.Output);
        Assert.True(run.OutputContains("src/App/App.csproj could not be read"), run.Output);
    }

    /// <summary>With nothing found and a project unread, the honest answer is `unknown`.</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("linux")]
    public void An_unreadable_project_is_unknown_when_nothing_was_found()
    {
        using var repository = Fixtures.DotNetWithPassingTests();
        Unreadable(repository, "src/App/App.csproj");
        Unreadable(repository, "tests/App.Tests/App.Tests.csproj");

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.tests");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "unknown"), run.Output);
        Assert.False(Says(run, "not detected"), run.Output);
    }

    /// <summary>Makes a tracked file unreadable without changing what Git records for it.</summary>
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("linux")]
    private static void Unreadable(RepositoryFixture repository, string relativePath)
        => File.SetUnixFileMode(repository.Absolute(relativePath), UnixFileMode.None);

    [Fact]
    public void A_web_test_script_is_detected_evidence()
    {
        using var repository = Fixtures.WebApplication();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.tests");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "detected"), run.Output);
        Assert.True(run.OutputContains("package.json"), run.Output);
    }

    /// <summary>
    /// The central honesty requirement: a repository that has tests has not thereby been
    /// shown to have architecture tests.
    /// </summary>
    [Fact]
    public void A_test_project_without_architecture_semantics_is_not_architecture_protection()
    {
        using var repository = Fixtures.DotNetWithPassingTests();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.architecture");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("readiness gap"), run.Output);
        Assert.True(Says(run, "not detected"), run.Output);
        Assert.False(Says(run, "detected"), run.Output);
    }

    /// <summary>
    /// Not detected is a statement about what the harness looked for, never about what the
    /// repository has.
    /// </summary>
    [Fact]
    public void Not_detected_names_the_evidence_it_looked_for_and_claims_no_absence()
    {
        using var repository = Fixtures.DotNetWithPassingTests();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.architecture");

        Assert.True(run.OutputContains("NetArchTest"), run.Output);
        Assert.True(run.OutputContains("not proof"), run.Output);
    }

    [Fact]
    public void Recognized_architecture_evidence_is_detected()
    {
        using var repository = Fixtures.DotNetWithArchitectureTests();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.architecture");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "detected"), run.Output);
        Assert.True(run.OutputContains("tests/App.Architecture.Tests/App.Architecture.Tests.csproj"), run.Output);
    }

    [Fact]
    public void Recognized_integration_evidence_is_detected()
    {
        using var repository = Fixtures.DotNetWithIntegrationTests();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.integration");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "detected"), run.Output);
        Assert.True(run.OutputContains("tests/App.IntegrationTests/App.IntegrationTests.csproj"), run.Output);
    }

    [Fact]
    public void Recognized_web_integration_evidence_is_detected()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithIntegrationAndArchitectureEvidence)
            .Commit();

        var integration = HarnessCli.Run(repository.Path, "check", "--only", "capability.integration");
        Assert.Equal(0, integration.ExitCode);
        Assert.True(Says(integration, "detected"), integration.Output);
        Assert.True(integration.OutputContains("test:e2e"), integration.Output);

        var architecture = HarnessCli.Run(repository.Path, "check", "--only", "capability.architecture");
        Assert.Equal(0, architecture.ExitCode);
        Assert.True(Says(architecture, "detected"), architecture.Output);
        Assert.True(architecture.OutputContains("dependency-cruiser"), architecture.Output);
    }

    /// <summary>
    /// Evidence that does not settle the question is its own answer. It is not a denial,
    /// and in v0 an unreadable capability picture does not end the run.
    /// </summary>
    [Fact]
    public void Ambiguous_evidence_is_unknown_rather_than_a_denial()
    {
        using var repository = Fixtures.DotNetWithPassingTests()
            .WriteFile("First.sln", Fixtures.EmptySolution)
            .WriteFile("Second.sln", Fixtures.EmptySolution)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability.tests");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Says(run, "unknown"), run.Output);
        Assert.False(Says(run, "not detected"), run.Output);
        Assert.False(run.OutputContains("violation"), run.Output);
    }

    /// <summary>
    /// The recognized-evidence list is hard-coded, so it is always potentially behind the
    /// repository. Reporting the scope the evidence was found in keeps a project the
    /// evidence says nothing about from disappearing into a green line.
    /// </summary>
    [Fact]
    public void A_new_project_does_not_inherit_another_projects_architecture_evidence()
    {
        using var repository = Fixtures.DotNetWithArchitectureTests();

        var before = HarnessCli.Run(repository.Path, "check", "--only", "capability.architecture");
        Assert.True(before.OutputContains("2 other tracked .NET projects"), before.Output);

        repository
            .WriteFile("src/Payments/Payments.csproj", Fixtures.LibraryProject)
            .WriteFile("src/Payments/Ledger.cs", "namespace Payments;\n\npublic static class Ledger;\n")
            .Commit();

        var after = HarnessCli.Run(repository.Path, "check", "--only", "capability.architecture");

        Assert.Equal(0, after.ExitCode);
        Assert.True(after.OutputContains("3 other tracked .NET projects"), after.Output);
    }

    /// <summary>Capability evidence is advisory in v0: it never fails a run.</summary>
    [Fact]
    public void A_repository_with_no_capability_evidence_never_fails_the_run()
    {
        using var repository = Fixtures.DotNetLibrary();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("readiness gap"), run.Output);
        Assert.False(run.OutputContains("violation"), run.Output);
        Assert.False(run.OutputContains("FAIL"), run.Output);
    }

    /// <summary>
    /// Readiness is named capabilities with evidence, not a number that invites comparison
    /// between repositories that were never measured the same way.
    /// </summary>
    [Fact]
    public void Capability_output_is_bounded_and_produces_no_readiness_score()
    {
        using var repository = Fixtures.DotNetWithArchitectureTests();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability");

        // Each capability costs one line of verdict and no more, whatever it found.
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= HarnessCli.ConciseLineBudget(repository.Path), run.Output);
        Assert.False(run.OutputContains("score"), run.Output);
        Assert.False(run.OutputContains("%"), run.Output);
    }

    [Fact]
    public void Only_and_skip_select_capability_checks_independently()
    {
        using var repository = Fixtures.DotNetWithPassingTests();

        var onlyTests = HarnessCli.Run(repository.Path, "check", "--only", "capability.tests");
        Assert.Equal(0, onlyTests.ExitCode);
        Assert.True(Says(onlyTests, "detected"), onlyTests.Output);

        // The unselected capabilities are visible as skipped and report no evidence.
        Assert.False(Says(onlyTests, "not detected"), onlyTests.Output);

        var withoutTests = HarnessCli.Run(
            repository.Path, "check", "--only", "capability", "--skip", "capability.tests");
        Assert.Equal(0, withoutTests.ExitCode);
        Assert.True(withoutTests.OutputContains("skipped"), withoutTests.Output);
        Assert.True(withoutTests.OutputContains("capability.tests"), withoutTests.Output);
    }

    [Fact]
    public void Reading_capability_evidence_does_not_modify_tracked_content()
    {
        using var repository = Fixtures.DotNetWithArchitectureTests();

        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "capability");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, repository.TrackedState());
    }
}
