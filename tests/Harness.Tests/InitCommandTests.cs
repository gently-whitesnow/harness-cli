using System.Text.Json;

namespace Harness.Tests;

public sealed class InitCommandTests
{
    private static readonly string[] Questions =
        ["tests.unit", "tests.integration", "tests.architecture", "format", "lint", "build", "typecheck"];

    [Fact]
    public void Init_from_a_subdirectory_creates_an_untracked_unanswered_frame_at_the_git_root()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("src/App.cs", "sealed class App;")
            .Commit();
        Directory.CreateDirectory(repository.Absolute("src/Feature"));

        var run = HarnessCli.Run(repository.Absolute("src/Feature"), "init");

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.StandardError);
        Assert.Contains(repository.Absolute(".harness.json"), run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Review every answer", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ask the repository owner", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("rather than suppressing", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Track the file", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("harness check --verbose", run.StandardOutput, StringComparison.Ordinal);

        var path = repository.Absolute(".harness.json");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(repository.Absolute("src/Feature/.harness.json")));
        Assert.Equal("?? .harness.json\n", repository.Git("status", "--porcelain=v1"));

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal(Release.Current, root.GetProperty("version").GetString());
        Assert.Equal(
            Questions,
            root.GetProperty("answers").EnumerateObject().Select(answer => answer.Name));
        Assert.All(
            root.GetProperty("answers").EnumerateObject(),
            answer => Assert.Empty(answer.Value.EnumerateObject()));
        Assert.Empty(root.GetProperty("applicability").EnumerateObject());
        Assert.Equal(10, root.GetProperty("settings")
            .GetProperty("comments.csharp").GetProperty("minimumCommentLines").GetInt32());
        Assert.Equal(400, root.GetProperty("settings")
            .GetProperty("maintainability.csharp").GetProperty("fileLines").GetInt32());
        Assert.Equal(8, root.GetProperty("settings")
            .GetProperty("duplication.csharp").GetProperty("windowLines").GetInt32());
        Assert.Equal(24, root.GetProperty("settings")
            .GetProperty("duplication.csharp").GetProperty("minimumTokens").GetInt32());
        Assert.Equal("en", root.GetProperty("settings")
            .GetProperty("commits").GetProperty("language").GetString());
        Assert.True(root.GetProperty("settings")
            .GetProperty("commits").GetProperty("requireSetup").GetBoolean());
        Assert.Contains("harness-hooks", repository.Git("config", "--local", "--get", "core.hooksPath"));
        Assert.Empty(root.GetProperty("policy").EnumerateObject());
        Assert.Empty(root.GetProperty("suppress").EnumerateArray());
    }

    [Fact]
    public void Latest_writes_the_moving_version_marker()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.Run(repository.Path, "init", "--latest");

        Assert.Equal(0, run.ExitCode);
        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal("latest", document.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void Init_selects_the_commit_language_and_installs_its_template()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.Run(repository.Path, "init", "--language", "ru");

        Assert.Equal(0, run.ExitCode);
        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal("ru", document.RootElement.GetProperty("settings")
            .GetProperty("commits").GetProperty("language").GetString());
        var template = repository.Git("config", "--local", "--get", "commit.template").Trim();
        Assert.Contains("Контекст:", File.ReadAllText(template), StringComparison.Ordinal);
    }

    [Fact]
    public void Initialized_frame_is_an_explicit_red_worklist_after_it_is_tracked()
    {
        using var repository = RepositoryFixture.CreateGitRepository();
        Assert.Equal(0, HarnessCli.Run(repository.Path, "init").ExitCode);
        repository.CommitAs("chore(harness): initialize repository frame");

        var check = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame");

        Assert.Equal(2, check.ExitCode);
        foreach (var question in Questions)
        {
            Assert.Contains($"frame.{question}", check.Output, StringComparison.Ordinal);
        }

        Assert.Equal(Questions.Length, Occurrences(check.Output, "outcome: incomplete"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Init_never_overwrites_an_existing_frame(bool tracked)
    {
        const string existing = "keep exactly this\n";
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile(".harness.json", existing);
        if (tracked)
        {
            repository.Commit();
        }

        var before = repository.Git("status", "--porcelain=v1");
        var run = HarnessCli.Run(repository.Path, "init");

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(run.StandardOutput);
        Assert.Contains("Refusing to overwrite", run.StandardError, StringComparison.Ordinal);
        Assert.Equal(existing, File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal(before, repository.Git("status", "--porcelain=v1"));
    }

    [Fact]
    public void Init_does_not_replace_a_tracked_frame_deleted_from_the_working_tree()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile(".harness.json", "tracked\n")
            .Commit()
            .Remove(".harness.json");

        var run = HarnessCli.Run(repository.Path, "init");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Refusing to overwrite", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.Absolute(".harness.json")));
    }

    [Fact]
    public void Init_outside_git_fails_without_creating_a_frame()
    {
        using var directory = TemporaryDirectory.Create();

        var run = HarnessCli.Run(directory.Path, "init");

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(run.StandardOutput);
        Assert.Contains("not inside a Git repository", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(directory.Absolute(".harness.json")));
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("--language", "de")]
    [InlineData("--latest", "--latest")]
    [InlineData("one", "two")]
    public void Invalid_init_arguments_show_usage_and_fail(params string[] arguments)
    {
        using var repository = RepositoryFixture.CreateGitRepository();
        var command = arguments.Prepend("init").ToArray();

        var run = HarnessCli.Run(repository.Path, command);

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(run.StandardOutput);
        Assert.Contains("harness init [path] [--latest]", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.Absolute(".harness.json")));
    }

    private static int Occurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;
}
