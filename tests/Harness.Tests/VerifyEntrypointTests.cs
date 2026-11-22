namespace Harness.Tests;

public sealed class VerifyEntrypointTests
{
    [Fact]
    public void Executable_tracked_script_invoking_harness_check_passes()
    {
        using var repo = Fixtures.Compliant();

        var run = HarnessCli.RunVerbose(repo.Path, "check", "--only", "verify.entrypoint");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("verify.entrypoint"), run.Output);
    }

    [Fact]
    public void Variable_invocation_and_dotnet_host_project_invocation_both_pass()
    {
        using var repo = Fixtures.Compliant()
            .WriteExecutable("verify.sh", "#!/usr/bin/env sh\nset -eu\n\"$HARNESS\" check --verbose\n")
            .Commit();

        var run = HarnessCli.Run(repo.Path, "check", "--only", "verify.entrypoint");
        Assert.Equal(0, run.ExitCode);

        repo.WriteExecutable("verify.sh", "#!/usr/bin/env sh\nset -eu\ndotnet run --project src/Harness/Host/Harness.Host.csproj -- check\n")
            .Commit();

        var hostRun = HarnessCli.Run(repo.Path, "check", "--only", "verify.entrypoint");
        Assert.Equal(0, hostRun.ExitCode);
    }

    [Fact]
    public void Non_executable_mode_100644_blocks()
    {
        using var repo = Fixtures.Compliant();
        repo.Git("update-index", "--chmod=-x", "verify.sh");

        var run = HarnessCli.RunVerbose(repo.Path, "check", "--only", "verify.entrypoint");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("100644"), run.Output);
        Assert.True(run.OutputContains("100755"), run.Output);
    }

    [Fact]
    public void Comment_only_mention_of_harness_check_blocks()
    {
        using var repo = Fixtures.Compliant()
            .WriteExecutable("verify.sh", "#!/usr/bin/env sh\n# harness check runs elsewhere\necho ok\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repo.Path, "check", "--only", "verify.entrypoint");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("does not invoke `harness check`"), run.Output);
    }

    [Fact]
    public void Missing_or_untracked_verify_script_blocks_and_names_index_gap()
    {
        using var repo = Fixtures.Compliant();
        repo.Git("rm", "--cached", "verify.sh");

        var run = HarnessCli.RunVerbose(repo.Path, "check", "--only", "verify.entrypoint");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("not in the index"), run.Output);
    }

    [Fact]
    public void Custom_script_path_in_settings_is_verified()
    {
        var frame = Frame.AllPresent().Settings("""{ "verify.entrypoint": { "path": "scripts/verify.sh" } }""");
        using var repo = Fixtures.Compliant(frame)
            .WriteExecutable("scripts/verify.sh", "#!/usr/bin/env sh\nset -eu\nharness check\n")
            .Commit();

        var run = HarnessCli.Run(repo.Path, "check", "--only", "verify.entrypoint");

        Assert.Equal(0, run.ExitCode);
    }
}
