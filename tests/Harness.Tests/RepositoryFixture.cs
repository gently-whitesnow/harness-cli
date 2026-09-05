using System.Text;

namespace Harness.Tests;

/// <summary>
/// A disposable Git repository on disk. Fixtures use real files, real symbolic links
/// and a real Git index so the harness sees the same evidence it sees in production.
/// </summary>
public sealed class RepositoryFixture : TemporaryDirectory
{
    private RepositoryFixture(string path) : base(path)
    {
    }

    public static RepositoryFixture CreateGitRepository()
    {
        var fixture = new RepositoryFixture(CreatePath());
        fixture.Git("init", "--quiet", "--initial-branch=main");
        fixture.Git("config", "user.email", "harness@example.com");
        fixture.Git("config", "user.name", "Harness Fixture");
        fixture.Git("config", "core.symlinks", "true");

        // Every fixture is an installed clone, so the hook resolves the binary under test.
        return fixture.InstallCloneLocalHarness();
    }

    public RepositoryFixture WriteFile(string relativePath, string content)
    {
        var absolutePath = Absolute(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, content);
        return this;
    }

    public RepositoryFixture Remove(string relativePath)
    {
        File.Delete(Absolute(relativePath));
        return this;
    }

    public RepositoryFixture PointIndexAtMissingObject(string relativePath)
    {
        Git("update-index", "--cacheinfo", "100644", new string('1', 40), relativePath);
        return this;
    }

    public RepositoryFixture WriteLines(string relativePath, int lineCount)
    {
        var builder = new StringBuilder();
        for (var line = 1; line <= lineCount; line++)
        {
            builder.Append("line ").Append(line).Append('\n');
        }

        return WriteFile(relativePath, builder.ToString());
    }

    public RepositoryFixture WriteSymbolicLink(string relativePath, string target)
    {
        var absolutePath = Absolute(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath)!);
        File.CreateSymbolicLink(absolutePath, target);
        return this;
    }

    /// <summary>Gives every Application slice an input mirror the fixture did not write itself.</summary>
    public RepositoryFixture WithInputMirrors()
    {
        foreach (var application in Directory.EnumerateDirectories(Path, "Application", SearchOption.AllDirectories))
        {
            var zone = Directory.GetParent(application)!.FullName;
            foreach (var top in Directory.EnumerateDirectories(application)
                .Where(directory => System.IO.Path.GetFileName(directory) != "Contracts"))
            {
                var directContent = Directory.EnumerateFiles(top)
                    .Any(file => System.IO.Path.GetFileName(file) is not ".gitkeep" and not ".keep" and not ".gitignore");
                var slices = directContent || Directory.Exists(System.IO.Path.Combine(top, "Contracts"))
                    ? new[] { top }
                    : Directory.EnumerateDirectories(top).ToArray();
                foreach (var slice in slices)
                {
                    var relativeSlice = System.IO.Path.GetRelativePath(application, slice);
                    var apiMirror = System.IO.Path.Combine(zone, "Api", relativeSlice);
                    var consumerMirror = System.IO.Path.Combine(zone, "Consumers", relativeSlice);
                    if (!Directory.Exists(apiMirror) && !Directory.Exists(consumerMirror))
                    {
                        var mirror = Directory.Exists(System.IO.Path.Combine(zone, "Consumers"))
                            && !Directory.Exists(System.IO.Path.Combine(zone, "Api"))
                            ? consumerMirror
                            : apiMirror;
                        WriteFile(
                            System.IO.Path.GetRelativePath(Path, System.IO.Path.Combine(mirror, ".fixture")),
                            "mirror");
                    }
                }
            }
        }

        return this;
    }

    /// <summary>Puts a harness where `install.sh --scope clone` puts it, and the hook looks first.</summary>
    public RepositoryFixture InstallCloneLocalHarness(string? content = null)
    {
        var directory = System.IO.Path.Combine(CommonGitDirectory(), "harness", "bin");
        Directory.CreateDirectory(directory);
        var binary = System.IO.Path.Combine(directory, "harness");
        File.Delete(binary);
        if (content is null)
        {
            File.CreateSymbolicLink(binary, HarnessCli.Executable);
        }
        else
        {
            File.WriteAllText(binary, content);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                binary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return this;
    }

    public string CommonGitDirectory()
    {
        var reported = Git("rev-parse", "--git-common-dir").Trim();
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, reported));
    }

    public string ManagedHookPath()
        => System.IO.Path.Combine(CommonGitDirectory(), "harness-hooks", "commit-msg");

    /// <summary>Stages everything and commits, so tracked state is unambiguous.</summary>
    public RepositoryFixture Commit()
    {
        Git("add", "--all");
        Git("commit", "--quiet", "--allow-empty", "--message", "fixture");
        return this;
    }

    public RepositoryFixture CommitAs(string message)
    {
        Git("add", "--all");
        Git("commit", "--quiet", "--allow-empty", "--message", message);
        return this;
    }

    public string Git(params string[] arguments)
    {
        var run = ProcessLauncher.Run("git", arguments, Path);
        if (run.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {run.StandardError}");
        }

        return run.StandardOutput;
    }

    /// <summary>Fingerprint of tracked content and Git state, used to prove the harness observes only.</summary>
    public string TrackedState()
        => Git("status", "--porcelain=v1") + Git("ls-files", "--stage") + Git("rev-parse", "HEAD");
}
