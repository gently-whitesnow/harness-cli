using System.Runtime.Versioning;

namespace Harness.Tests;

/// <summary>
/// Evidence that cannot be read reliably must end the run as incomplete rather than as a
/// pass or as a repository violation. The controlled executables come from the test
/// process environment, so the real process boundary is exercised.
/// </summary>
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("linux")]
public sealed class EnvironmentFailureTests
{
    [Fact]
    public void A_missing_git_executable_is_incomplete()
    {
        using var repository = Fixtures.Compliant();
        using var emptyPath = TemporaryDirectory.Create();

        var run = HarnessCli.Run(
            repository.Path,
            new Dictionary<string, string> { ["PATH"] = emptyPath.Path },
            "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("git"), run.Output);
    }

    [Fact]
    public void A_failing_git_executable_is_incomplete()
    {
        using var repository = Fixtures.Compliant();
        using var stubs = TemporaryDirectory.Create();
        WriteStubGit(stubs, "#!/bin/sh\necho 'fatal: cannot read the index' >&2\nexit 128\n");

        var run = HarnessCli.Run(
            repository.Path,
            new Dictionary<string, string> { ["PATH"] = stubs.Path },
            "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("INCOMPLETE"), run.Output);
    }

    [Fact]
    public void Malformed_git_output_is_incomplete()
    {
        using var repository = Fixtures.Compliant();
        using var stubs = TemporaryDirectory.Create();
        WriteStubGit(
            stubs,
            $"""
            #!/bin/sh
            case "$1" in
              rev-parse) echo '{repository.Path}' ;;
              ls-files) printf 'not-an-index-record\0' ;;
              *) exit 0 ;;
            esac
            """);

        var run = HarnessCli.Run(
            repository.Path,
            new Dictionary<string, string> { ["PATH"] = stubs.Path },
            "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("INCOMPLETE"), run.Output);
    }

    private static void WriteStubGit(TemporaryDirectory directory, string script)
    {
        var path = directory.Absolute("git");
        File.WriteAllText(path, script);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
