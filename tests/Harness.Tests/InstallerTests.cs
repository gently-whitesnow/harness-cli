using System.Security.Cryptography;

namespace Harness.Tests;

/// <summary>Exercises installation as a process boundary, including the Git process after it exits.</summary>
[Trait("Category", "Publication")]
public sealed class InstallerTests
{
    [Fact]
    public void Fresh_clone_install_sets_up_a_host_commit_without_touching_tracked_or_user_files()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var source = Repository();
        using var sandbox = TemporaryDirectory.Create();
        using var assets = InstallerAssets.Create();
        var clone = Clone(source.Path, sandbox.Absolute("fresh"));
        var trackedBefore = Git(clone, "ls-files", "--stage").StandardOutput;
        var headBefore = Git(clone, "rev-parse", "HEAD").StandardOutput;

        var install = RunInstaller(clone, assets);

        Assert.Equal(0, install.ExitCode);
        var binary = CloneBinary(clone);
        Assert.True(File.Exists(binary));
        Assert.False(Directory.Exists(assets.UserInstallDirectory));
        Assert.Equal(trackedBefore, Git(clone, "ls-files", "--stage").StandardOutput);
        Assert.Equal(headBefore, Git(clone, "rev-parse", "HEAD").StandardOutput);
        Assert.Equal("", Git(clone, "status", "--porcelain=v1").StandardOutput);

        // The installer puts the binary where the hook looks for it, and the hook holds no path.
        var hook = File.ReadAllText(Path.Combine(clone, ".git", "harness-hooks", "commit-msg"));
        Assert.DoesNotContain(binary, hook, StringComparison.Ordinal);
        Assert.Contains("harness/bin/harness", hook, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(clone, "host-change.txt"), "installed process has exited\n");
        Assert.Equal(0, Git(clone, "add", "host-change.txt").ExitCode);
        var rejected = Git(clone, "commit", "--message", "unstructured message");
        Assert.Equal(1, rejected.ExitCode);
        Assert.Equal(headBefore, Git(clone, "rev-parse", "HEAD").StandardOutput);
        var commit = Git(clone, "commit", "--message", "test(installer): verify host commit");
        Assert.Equal(0, commit.ExitCode);
    }

    [Fact]
    public void Linked_worktree_uses_the_clone_common_directory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var source = Repository();
        using var sandbox = TemporaryDirectory.Create();
        using var assets = InstallerAssets.Create();
        var clone = Clone(source.Path, sandbox.Absolute("main"));
        var linked = sandbox.Absolute("linked");
        Assert.Equal(0, Git(clone, "worktree", "add", "--quiet", "-b", "linked", linked).ExitCode);

        var install = RunInstaller(linked, assets);

        Assert.Equal(0, install.ExitCode);
        var binary = CloneBinary(clone);
        Assert.True(File.Exists(binary));
        Assert.False(Directory.Exists(Path.Combine(linked, "harness")));
        var check = ProcessLauncher.Run(binary, ["check", "--only", "commits.setup"], linked);
        Assert.Equal(0, check.ExitCode);
    }

    [Fact]
    public async Task Parallel_clone_installations_are_serialized()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var source = Repository();
        using var sandbox = TemporaryDirectory.Create();
        using var assets = InstallerAssets.Create(downloadDelay: "2");
        var clone = Clone(source.Path, sandbox.Absolute("parallel"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            await start.Task;
            return RunInstaller(clone, assets);
        });
        var second = Task.Run(async () =>
        {
            await start.Task;
            return RunInstaller(clone, assets);
        });
        start.SetResult();
        var installations = await Task.WhenAll(first, second);

        Assert.Equal(0, installations[0].ExitCode);
        Assert.Equal(0, installations[1].ExitCode);
        Assert.Contains(
            "Waiting for another clone-local harness installation",
            installations[0].Output + installations[1].Output,
            StringComparison.Ordinal);
        Assert.True(File.Exists(CloneBinary(clone)));
        Assert.False(Directory.Exists(Path.Combine(clone, ".git", "harness", "install.lock")));
    }

    [Fact]
    public void Failed_checksum_does_not_replace_the_installed_binary()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var source = Repository();
        using var sandbox = TemporaryDirectory.Create();
        using var assets = InstallerAssets.Create();
        var clone = Clone(source.Path, sandbox.Absolute("checksum"));
        Assert.Equal(0, RunInstaller(clone, assets).ExitCode);
        var binary = CloneBinary(clone);
        var installedHash = SHA256.HashData(File.ReadAllBytes(binary));
        File.WriteAllText(assets.ChecksumPath, new string('0', 64) + "\n");

        var reinstall = RunInstaller(clone, assets);

        Assert.Equal(1, reinstall.ExitCode);
        Assert.Equal(installedHash, SHA256.HashData(File.ReadAllBytes(binary)));
    }

    private static RepositoryFixture Repository()
        => RepositoryFixture.CreateGitRepository()
            .WriteFile("AGENTS.md", "# Root\n\nNavigation.\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("CLAUDE.md", "AGENTS.md")
            .WriteFile(
                ".harness.json",
                Frame.AllPresent()
                    .Settings("""{ "commits": { "language": "en", "requireSetup": true } }""")
                    .ToString())
            .Commit();

    private static string Clone(string source, string destination)
    {
        var parent = Path.GetDirectoryName(destination)!;
        var clone = ProcessLauncher.Run("git", ["clone", "--quiet", source, destination], parent);
        Assert.Equal(0, clone.ExitCode);
        Assert.Equal(0, Git(destination, "config", "user.email", "harness@example.com").ExitCode);
        Assert.Equal(0, Git(destination, "config", "user.name", "Harness Fixture").ExitCode);
        return destination;
    }

    private static CliRun RunInstaller(string repository, InstallerAssets assets)
        => ProcessLauncher.Run(
            "/bin/sh",
            [Path.Combine(Release.RepositoryRoot(), "install.sh"), "--scope", "clone"],
            repository,
            assets.Environment);

    private static CliRun Git(string repository, params string[] arguments)
        => ProcessLauncher.Run("git", arguments, repository);

    private static string CloneBinary(string repository)
    {
        var commonDirectory = Git(repository, "rev-parse", "--git-common-dir").StandardOutput.Trim();
        if (!Path.IsPathFullyQualified(commonDirectory))
        {
            commonDirectory = Path.GetFullPath(commonDirectory, repository);
        }

        commonDirectory = ProcessLauncher.Run("/bin/pwd", ["-P"], commonDirectory).StandardOutput.Trim();
        return Path.Combine(commonDirectory, "harness", "bin", "harness");
    }

    private sealed class InstallerAssets : IDisposable
    {
        private readonly TemporaryDirectory directory;

        private InstallerAssets(TemporaryDirectory directory, string checksumPath, string? downloadDelay)
        {
            this.directory = directory;
            ChecksumPath = checksumPath;
            UserInstallDirectory = directory.Absolute("must-not-be-created");
            Environment = new Dictionary<string, string>
            {
                ["PATH"] = directory.Absolute("bin") + Path.PathSeparator
                    + System.Environment.GetEnvironmentVariable("PATH"),
                ["HOME"] = directory.Absolute("home"),
                ["HARNESS_INSTALL_DIR"] = UserInstallDirectory,
                ["HARNESS_TEST_ARCHIVE"] = directory.Absolute("harness.tar.gz"),
                ["HARNESS_TEST_CHECKSUM"] = checksumPath,
                ["HARNESS_TEST_DOWNLOAD_DELAY"] = downloadDelay ?? "",
                ["HARNESS_VERSION"] = Release.Current,
            };
        }

        public IReadOnlyDictionary<string, string> Environment { get; }

        public string ChecksumPath { get; }

        public string UserInstallDirectory { get; }

        public static InstallerAssets Create(string? downloadDelay = null)
        {
            var directory = TemporaryDirectory.Create();
            var payloadDirectory = directory.Absolute("payload");
            Directory.CreateDirectory(payloadDirectory);
            var published = NativePublication.Get().Executable;
            var payload = Path.Combine(payloadDirectory, "harness");
            File.Copy(published, payload);

            var archive = directory.Absolute("harness.tar.gz");
            var tar = ProcessLauncher.Run("tar", ["-czf", archive, "-C", payloadDirectory, "harness"], directory.Path);
            Assert.Equal(0, tar.ExitCode);
            var checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant();
            var checksumPath = directory.Absolute("harness.tar.gz.sha256");
            File.WriteAllText(checksumPath, checksum + "\n");

            var bin = directory.Absolute("bin");
            Directory.CreateDirectory(bin);
            var curl = Path.Combine(bin, "curl");
            File.WriteAllText(
                curl,
                """
                #!/bin/sh
                set -eu
                output=""
                url=""
                while [ "$#" -gt 0 ]; do
                  case "$1" in
                    -o) output="$2"; shift 2 ;;
                    -*) shift ;;
                    *) url="$1"; shift ;;
                  esac
                done
                case "$url" in
                  *.sha256) cp "$HARNESS_TEST_CHECKSUM" "$output" ;;
                  *)
                    if [ -n "$HARNESS_TEST_DOWNLOAD_DELAY" ]; then
                      sleep "$HARNESS_TEST_DOWNLOAD_DELAY"
                    fi
                    cp "$HARNESS_TEST_ARCHIVE" "$output"
                    ;;
                esac
                """);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    curl,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            return new InstallerAssets(directory, checksumPath, downloadDelay);
        }

        public void Dispose() => directory.Dispose();
    }
}
