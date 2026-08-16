using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Harness.Tests;

/// <summary>
/// The web adapter as a caller sees it: a package manager chosen from repository evidence,
/// the repository's own non-mutating quality scripts executed through it, a missing script
/// reported as a readiness gap rather than invented, and exit translation that separates a
/// repository defect from an environment the harness cannot trust.
/// </summary>
public sealed class WebRepositoryTests
{
    /// <summary>`npm` as an invoked command, without also matching `pnpm`.</summary>
    private static bool Invokes(string output, string packageManager)
        => Regex.IsMatch(output, @"(?<![\w.-])" + Regex.Escape(packageManager) + @" run\b");

    [Fact]
    public void A_repository_without_a_web_surface_reports_the_gates_as_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
        Assert.True(run.OutputContains("web.build"), run.Output);

        // No stack means nothing to run, not an invented execution plan.
        Assert.False(Invokes(run.Output, "npm"), run.Output);
    }

    [Fact]
    public void A_green_web_repository_passes_and_shows_every_command_it_ran()
    {
        using var repository = Fixtures.WebApplication();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("npm run format:check"), run.Output);
        Assert.True(run.OutputContains("npm run lint"), run.Output);
        Assert.True(run.OutputContains("npm run typecheck"), run.Output);
        Assert.True(run.OutputContains("npm run test"), run.Output);
        Assert.True(run.OutputContains("npm run build"), run.Output);
        Assert.True(run.OutputContains("exit 0"), run.Output);
    }

    /// <summary>
    /// The repository's lockfile is the evidence. A package manager the caller happens to
    /// prefer is not, or the same repository would be verified differently per developer.
    /// </summary>
    [Fact]
    public void The_package_manager_comes_from_the_lockfile_not_from_the_callers_preference()
    {
        using var repository = Fixtures.WebApplication();

        var run = HarnessCli.Run(
            repository.Path,
            new Dictionary<string, string> { ["npm_config_user_agent"] = "pnpm/9.0.0 npm/? node/v22.0.0" },
            "check",
            "--only",
            "web.lint");

        Assert.Equal(0, run.ExitCode);
        Assert.True(Invokes(run.Output, "npm"), run.Output);
        Assert.False(Invokes(run.Output, "pnpm"), run.Output);
    }

    /// <summary>
    /// A pnpm lockfile selects pnpm. The environment may not have pnpm, so this asserts the
    /// resolved plan rather than the exit code: either pnpm ran, or the run is incomplete
    /// because pnpm is missing. In both cases npm is never substituted for it.
    /// </summary>
    [Fact]
    public void A_pnpm_lockfile_selects_pnpm()
    {
        using var repository = Fixtures.WebApplication()
            .Remove("package-lock.json")
            .WriteFile("pnpm-lock.yaml", Fixtures.PnpmLockFile)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.lint");

        Assert.True(run.OutputContains("pnpm"), run.Output);
        Assert.False(Invokes(run.Output, "npm"), run.Output);
    }

    [Fact]
    public void Two_lockfiles_are_conflicting_evidence_rather_than_a_choice()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("pnpm-lock.yaml", Fixtures.PnpmLockFile)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("package-lock.json"), run.Output);
        Assert.True(run.OutputContains("pnpm-lock.yaml"), run.Output);
        Assert.False(run.OutputContains("violation"), run.Output);
    }

    /// <summary>
    /// A manifest alone does not say which package manager resolved it, and the harness has
    /// no default to fall back on: a default would be a guess wearing a convention's clothes.
    /// </summary>
    [Fact]
    public void A_manifest_with_no_package_manager_evidence_is_incomplete()
    {
        using var repository = Fixtures.WebApplication()
            .Remove("package-lock.json")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.lint");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("package.json"), run.Output);
        Assert.False(Invokes(run.Output, "npm"), run.Output);
    }

    [Fact]
    public void A_package_manager_declaration_is_evidence_when_there_is_no_lockfile()
    {
        using var repository = Fixtures.WebApplication()
            .Remove("package-lock.json")
            .WriteFile("package.json", Fixtures.WebManifestDeclaring("npm@10.9.2"))
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.lint");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("npm run lint"), run.Output);
    }

    [Fact]
    public void A_declaration_that_disagrees_with_the_lockfile_is_conflicting_evidence()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestDeclaring("pnpm@9.1.0"))
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.lint");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("pnpm"), run.Output);
        Assert.True(run.OutputContains("package-lock.json"), run.Output);
        Assert.False(Invokes(run.Output, "npm"), run.Output);
    }

    [Fact]
    public void A_lint_defect_is_a_violation()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithFailingLint)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.lint");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("npm run lint"), run.Output);
        Assert.True(run.OutputContains("violation"), run.Output);
    }

    [Fact]
    public void A_typecheck_defect_is_located_where_the_compiler_reported_it()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithFailingTypecheck)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.typecheck");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/main.ts:3"), run.Output);
        Assert.True(run.OutputContains("TS2322"), run.Output);
    }

    /// <summary>Every blocking gate must be able to prove the state it exists to detect.</summary>
    [Theory]
    [InlineData("web.format", "npm run format:check")]
    [InlineData("web.test", "npm run test")]
    [InlineData("web.build", "npm run build")]
    public void A_defect_in_any_web_gate_is_a_violation(string identifier, string command)
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithFailingScripts)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", identifier);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains(command), run.Output);
        Assert.True(run.OutputContains("violation"), run.Output);
    }

    /// <summary>
    /// Missing infrastructure is not a code defect, and it is not permission to invent a
    /// repository-specific command either.
    /// </summary>
    [Fact]
    public void A_missing_standard_script_is_a_readiness_gap()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithoutTypecheck)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.typecheck");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("readiness gap"), run.Output);
        Assert.True(run.OutputContains("typecheck"), run.Output);
        Assert.False(run.OutputContains("PASS"), run.Output);
        Assert.False(Invokes(run.Output, "npm"), run.Output);
    }

    [Fact]
    public void A_mutating_format_script_is_reported_rather_than_run()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithMutatingFormat)
            .Commit();

        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.format");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("readiness gap"), run.Output);
        Assert.True(run.OutputContains("--write"), run.Output);
        Assert.False(run.OutputContains("npm run format"), run.Output);
        Assert.Equal(before, repository.TrackedState());
    }

    /// <summary>
    /// A verification script that hands the work to the fixer is still the fixer. Reading
    /// only the script the gate selected would let the delegation through the guard.
    /// </summary>
    [Fact]
    public void A_verification_script_that_delegates_to_the_fixer_is_not_run()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithDelegatedMutatingFormat)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.format");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("readiness gap"), run.Output);

        // The report quotes the offending command; what matters is that nothing ran it.
        Assert.False(run.OutputContains("ran "), run.Output);
    }

    /// <summary>
    /// A run that verified what it could, next to a gate that had nothing to verify with, is
    /// not the same as a repository with every quality command in place.
    /// </summary>
    [Fact]
    public void A_run_with_a_readiness_gap_does_not_read_as_fully_covered()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithoutTypecheck)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("PASS WITH GAPS"), run.Output);
        Assert.True(run.OutputContains("npm run lint"), run.Output);
    }

    /// <summary>
    /// Declared dependencies that are not installed say nothing about the code. The harness
    /// does not install them, so the run is incomplete rather than a fabricated defect.
    /// </summary>
    [Fact]
    public void Declared_dependencies_that_are_not_installed_are_incomplete()
    {
        using var repository = Fixtures.WebApplication()
            .WriteFile("package.json", Fixtures.WebManifestWithDependencies)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web.build");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("INCOMPLETE"), run.Output);
        Assert.True(run.OutputContains("node_modules"), run.Output);
        Assert.False(run.OutputContains("violation"), run.Output);
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("linux")]
    public void An_unusable_package_manager_is_incomplete_and_does_not_blame_the_repository()
    {
        using var repository = Fixtures.WebApplication();
        using var stubs = TemporaryDirectory.Create();
        Stub(stubs, "npm", "#!/bin/sh\necho 'npm: command not found' >&2\nexit 127\n");

        var run = HarnessCli.Run(
            repository.Path,
            new Dictionary<string, string> { ["PATH"] = stubs.Path + ":" + Environment.GetEnvironmentVariable("PATH") },
            "check",
            "--only",
            "web");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("INCOMPLETE"), run.Output);
        Assert.True(run.OutputContains("npm"), run.Output);
        Assert.False(run.OutputContains("violation"), run.Output);
    }

    [Fact]
    public void Only_and_skip_select_web_gates_independently()
    {
        using var repository = Fixtures.WebApplication();

        var onlyBuild = HarnessCli.Run(repository.Path, "check", "--only", "web.build");
        Assert.Equal(0, onlyBuild.ExitCode);
        Assert.True(onlyBuild.OutputContains("npm run build"), onlyBuild.Output);
        Assert.False(onlyBuild.OutputContains("npm run lint"), onlyBuild.Output);

        var withoutLint = HarnessCli.Run(repository.Path, "check", "--only", "web", "--skip", "web.lint");
        Assert.Equal(0, withoutLint.ExitCode);
        Assert.True(withoutLint.OutputContains("skipped"), withoutLint.Output);
        Assert.True(withoutLint.OutputContains("web.lint"), withoutLint.Output);
        Assert.False(withoutLint.OutputContains("npm run lint"), withoutLint.Output);
    }

    [Fact]
    public void Checking_a_web_repository_does_not_modify_tracked_source_configuration_or_the_lockfile()
    {
        using var repository = Fixtures.WebApplication();

        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "web");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, repository.TrackedState());
    }

    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("linux")]
    private static void Stub(TemporaryDirectory directory, string name, string script)
    {
        var path = directory.Absolute(name);
        File.WriteAllText(path, script);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
