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
        return fixture;
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
