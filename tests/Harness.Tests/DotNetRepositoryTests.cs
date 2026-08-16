using System.Runtime.Versioning;

namespace Harness.Tests;

/// <summary>
/// The .NET adapter as a caller sees it: discovery of an execution plan, verification-only
/// formatting, build and test gates through the installed SDK, and exit translation that
/// separates a repository violation from an environment the harness cannot trust.
/// </summary>
public sealed class DotNetRepositoryTests
{
    [Fact]
    public void A_repository_without_a_dotnet_surface_reports_the_gates_as_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
        Assert.True(run.OutputContains("dotnet.build"), run.Output);

        // No stack means nothing to run, not an invented execution plan.
        Assert.False(run.OutputContains("dotnet build"), run.Output);
    }

    [Fact]
    public void A_green_dotnet_repository_passes_and_shows_every_command_it_ran()
    {
        using var repository = Fixtures.DotNetLibrary();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("dotnet format src/App/App.csproj"), run.Output);
        Assert.True(run.OutputContains("dotnet build src/App/App.csproj"), run.Output);
    }

    [Fact]
    public void Formatting_runs_only_in_verification_mode()
    {
        using var repository = Fixtures.DotNetLibrary();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.format");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("--verify-no-changes"), run.Output);
    }

    [Fact]
    public void A_formatting_violation_fails_and_localizes_the_evidence()
    {
        using var repository = Fixtures.DotNetLibrary()
            .WriteFile("src/App/Widget.cs", Fixtures.MisformattedSource)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.format");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/App/Widget.cs"), run.Output);
    }

    /// <summary>
    /// The SDK localizes its diagnostics. Evidence the harness reports must be the same
    /// text for every caller, so a finding stays comparable, greppable and reviewable.
    /// </summary>
    [Fact]
    public void Evidence_does_not_depend_on_the_callers_locale()
    {
        using var repository = Fixtures.DotNetLibrary()
            .WriteFile("src/App/Widget.cs", Fixtures.MisformattedSource)
            .Commit();

        var run = HarnessCli.Run(
            repository.Path,
            new Dictionary<string, string> { ["LANG"] = "ru_RU.UTF-8", ["LC_ALL"] = "ru_RU.UTF-8" },
            "check",
            "--only",
            "dotnet.format");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("Fix whitespace formatting"), run.Output);
    }

    [Fact]
    public void A_formatting_violation_leaves_the_offending_file_untouched()
    {
        using var repository = Fixtures.DotNetLibrary()
            .WriteFile("src/App/Widget.cs", Fixtures.MisformattedSource)
            .Commit();

        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.format");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(Fixtures.MisformattedSource, File.ReadAllText(repository.Absolute("src/App/Widget.cs")));
        Assert.Equal(before, repository.TrackedState());
    }

    [Fact]
    public void A_compilation_error_fails_and_localizes_the_evidence()
    {
        using var repository = Fixtures.DotNetLibrary()
            .WriteFile("src/App/Widget.cs", Fixtures.UncompilableSource)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.build");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/App/Widget.cs"), run.Output);
        Assert.True(run.OutputContains("CS0029"), run.Output);
    }

    [Fact]
    public void A_repository_without_a_test_project_reports_the_test_gate_as_not_applicable()
    {
        using var repository = Fixtures.DotNetLibrary();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.test");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
        Assert.False(run.OutputContains("dotnet test"), run.Output);
    }

    [Fact]
    public void A_passing_test_project_passes()
    {
        using var repository = Fixtures.DotNetWithPassingTests();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.test");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("dotnet test tests/App.Tests/App.Tests.csproj"), run.Output);
    }

    [Fact]
    public void A_failing_test_is_a_violation()
    {
        using var repository = Fixtures.DotNetWithPassingTests()
            .WriteFile("tests/App.Tests/WidgetTests.cs", Fixtures.FailingTest)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.test");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("Widget_has_a_size"), run.Output);
    }

    /// <summary>
    /// A tracked lock file must constrain restore without being mistaken for a violation:
    /// the restore constraint is an MSBuild property, and not every gate is an MSBuild
    /// command, so applying it blindly would fabricate a formatting failure.
    /// </summary>
    [Fact]
    public void A_tracked_lock_file_does_not_fabricate_a_violation()
    {
        using var repository = Fixtures.DotNetLibrary()
            .WriteFile("src/App/packages.lock.json", Fixtures.EmptyLockFile)
            .Commit();

        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, repository.TrackedState());
    }

    /// <summary>
    /// Packages the environment cannot supply say nothing about the code, so the run ends
    /// as incomplete. Anything else lets a broken feed read as a repository defect.
    /// </summary>
    [Fact]
    public void A_package_restore_failure_is_incomplete_rather_than_a_violation()
    {
        using var repository = Fixtures.DotNetLibrary()
            .WriteFile("nuget.config", Fixtures.NoPackageSources)
            .WriteFile("src/App/App.csproj", Fixtures.LibraryNeedingAnUnavailablePackage)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.build");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("INCOMPLETE"), run.Output);
        Assert.True(run.OutputContains("NU"), run.Output);
        Assert.False(run.OutputContains("violation"), run.Output);
    }

    [Fact]
    public void More_than_one_tracked_solution_is_an_ambiguous_execution_plan()
    {
        using var repository = Fixtures.DotNetLibrary()
            .WriteFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\n")
            .WriteFile("Everything.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "dotnet");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("App.sln"), run.Output);
        Assert.True(run.OutputContains("Everything.sln"), run.Output);
    }

    [Fact]
    public void Only_and_skip_select_dotnet_gates_independently()
    {
        using var repository = Fixtures.DotNetLibrary();

        var onlyBuild = HarnessCli.Run(repository.Path, "check", "--only", "dotnet.build");
        Assert.Equal(0, onlyBuild.ExitCode);
        Assert.True(onlyBuild.OutputContains("dotnet build"), onlyBuild.Output);
        Assert.False(onlyBuild.OutputContains("dotnet format"), onlyBuild.Output);

        var withoutFormat = HarnessCli.Run(repository.Path, "check", "--skip", "dotnet.format");
        Assert.Equal(0, withoutFormat.ExitCode);
        Assert.True(withoutFormat.OutputContains("skipped"), withoutFormat.Output);
        Assert.True(withoutFormat.OutputContains("dotnet.format"), withoutFormat.Output);
        Assert.False(withoutFormat.OutputContains("dotnet format"), withoutFormat.Output);
    }

    [Fact]
    public void Checking_a_dotnet_repository_does_not_modify_tracked_content_or_git_state()
    {
        using var repository = Fixtures.DotNetWithPassingTests();

        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, repository.TrackedState());
    }

    /// <summary>
    /// A .NET repository the harness cannot verify because the SDK is unusable must end the
    /// run as incomplete; it must never be reported as a repository violation.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("linux")]
    public void An_unusable_dotnet_sdk_is_incomplete_and_does_not_blame_the_repository()
    {
        using var repository = Fixtures.DotNetLibrary();
        using var stubs = TemporaryDirectory.Create();
        StubExecutable(stubs, "dotnet", "#!/bin/sh\necho 'dotnet: command not found' >&2\nexit 127\n");

        var run = HarnessCli.Run(
            repository.Path,
            new Dictionary<string, string> { ["PATH"] = stubs.Path + ":" + Environment.GetEnvironmentVariable("PATH") },
            "check",
            "--only",
            "dotnet");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("INCOMPLETE"), run.Output);
        Assert.True(run.OutputContains("SDK"), run.Output);
        Assert.False(run.OutputContains("violation"), run.Output);
    }

    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("linux")]
    private static void StubExecutable(TemporaryDirectory directory, string name, string script)
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
