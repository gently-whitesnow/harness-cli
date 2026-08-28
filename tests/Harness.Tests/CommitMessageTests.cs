namespace Harness.Tests;

public sealed class CommitMessageTests
{
    [Fact]
    public void Explicit_profile_selects_russian_and_required_setup()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Settings(
            """{ "commits": { "language": "ru", "requireSetup": true } }"""));

        var template = HarnessCli.Run(repository.Path, "commit-message", "template");
        var setup = HarnessCli.RunVerbose(repository.Path, "check", "--only", "commits.setup");

        Assert.Equal(0, template.ExitCode);
        Assert.Contains("Контекст:", template.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(1, setup.ExitCode);
        Assert.Contains("harness setup", setup.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Russian_subject_and_structured_body_pass()
    {
        using var repository = Repository(RequireSetup: false, language: "ru")
            .WriteFile(
                "message.txt",
                """
                feat(task-trackers): унифицировать содержимое карточек

                Контекст:
                Провайдеры представляли содержимое по-разному.

                Решение:
                - Хранить единый многострочный text.

                Границы:
                - Локальный поиск не добавляется.

                Refs: HARNESS-142
                """);

        var run = HarnessCli.Run(repository.Path, "commit-message", "check", "message.txt");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("ERROR", run.StandardError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("feature(core): добавить кэш", "type 'feature'")]
    [InlineData("feat(BadScope): добавить кэш", "lowercase kebab-case")]
    [InlineData("feat(core): add cache", "must contain Cyrillic")]
    [InlineData("feat(core): добавить кэш.", "must not end with a period")]
    public void Invalid_subject_is_blocked(string message, string explanation)
    {
        using var repository = Repository(RequireSetup: false, language: "ru")
            .WriteFile("message.txt", message + "\n");

        var run = HarnessCli.Run(repository.Path, "commit-message", "check", "message.txt");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(explanation, run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Subject_over_50_characters_warns_but_subject_over_72_is_blocked()
    {
        using var repository = Repository(RequireSetup: false, language: "en");
        repository.WriteFile("message.txt", $"feat(core): {new string('a', 40)}\n");
        var warning = HarnessCli.Run(repository.Path, "commit-message", "check", "message.txt");

        repository.WriteFile("message.txt", $"feat(core): {new string('a', 70)}\n");
        var failure = HarnessCli.Run(repository.Path, "commit-message", "check", "message.txt");

        Assert.Equal(0, warning.ExitCode);
        Assert.Contains("WARNING", warning.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("50", warning.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(1, failure.ExitCode);
        Assert.Contains("maximum is 72", failure.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Nontrivial_body_requires_context_and_decision_sections()
    {
        using var repository = Repository(RequireSetup: false, language: "en")
            .WriteFile("message.txt", "fix(api): reject stale token\n\nA useful but unstructured explanation.\n");

        var run = HarnessCli.Run(repository.Path, "commit-message", "check", "message.txt");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Context:", run.StandardError, StringComparison.Ordinal);
        Assert.Contains("Decision:", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_is_idempotent_and_makes_the_required_check_pass()
    {
        using var repository = Repository(RequireSetup: true, language: "ru");

        var before = HarnessCli.RunVerbose(repository.Path, "check", "--only", "commits.setup");
        var first = HarnessCli.Run(repository.Path, "setup");
        var second = HarnessCli.Run(repository.Path, "setup");
        var after = HarnessCli.RunVerbose(repository.Path, "check", "--only", "commits.setup");

        Assert.Equal(1, before.ExitCode);
        Assert.Contains("harness setup", before.Output, StringComparison.Ordinal);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(0, after.ExitCode);
        Assert.Contains("commit-msg hook are active", after.Output, StringComparison.Ordinal);

        var template = repository.Git("config", "--local", "--get", "commit.template").Trim();
        Assert.Contains("Контекст:", File.ReadAllText(template), StringComparison.Ordinal);
    }

    [Fact]
    public void Installed_hook_blocks_an_invalid_message_before_commit()
    {
        using var repository = Repository(RequireSetup: true, language: "en");
        Assert.Equal(0, HarnessCli.Run(repository.Path, "setup").ExitCode);
        repository.WriteFile("change.txt", "change\n");
        repository.Git("add", "change.txt");
        var head = repository.Git("rev-parse", "HEAD");

        var commit = ProcessLauncher.Run(
            "git",
            ["commit", "--message", "unstructured message"],
            repository.Path);

        Assert.Equal(1, commit.ExitCode);
        Assert.Contains("expected '<type>(<scope>): <description>'", commit.StandardError, StringComparison.Ordinal);
        Assert.Equal(head, repository.Git("rev-parse", "HEAD"));
    }

    /// <summary>
    /// A linked worktree shares the clone's configuration, so it must share its setup too.
    /// Anchoring on the worktree's own metadata directory would make the check unsatisfiable
    /// there: `core.hooksPath` is written once, for every worktree at once.
    /// </summary>
    [Fact]
    public void Setup_of_the_clone_covers_a_linked_worktree()
    {
        using var repository = Repository(RequireSetup: true, language: "en");
        Assert.Equal(0, HarnessCli.Run(repository.Path, "setup").ExitCode);

        var worktree = repository.Absolute("linked");
        repository.Git("worktree", "add", "--quiet", "-b", "linked", worktree);

        var check = HarnessCli.RunVerbose(worktree, "check", "--only", "commits.setup");
        var setup = HarnessCli.Run(worktree, "setup");

        Assert.Equal(0, check.ExitCode);
        Assert.Contains("commit-msg hook are active", check.Output, StringComparison.Ordinal);
        Assert.Equal(0, setup.ExitCode);
    }

    [Fact]
    public void Setup_refuses_to_replace_an_unrelated_hooks_path()
    {
        using var repository = Repository(RequireSetup: true, language: "en");
        repository.Git("config", "--local", "core.hooksPath", ".custom-hooks");

        var run = HarnessCli.Run(repository.Path, "setup");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Refusing to replace", run.StandardError, StringComparison.Ordinal);
        Assert.Equal(".custom-hooks\n", repository.Git("config", "--local", "--get", "core.hooksPath"));
    }

    [Fact]
    public void Template_uses_the_configured_language()
    {
        using var repository = Repository(RequireSetup: false, language: "ru");

        var run = HarnessCli.Run(repository.Path, "commit-message", "template");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Контекст:", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Решение:", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("BREAKING CHANGE:", run.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Range_check_rejects_fixup_commits_that_the_local_hook_may_allow()
    {
        using var repository = Repository(RequireSetup: false, language: "en");
        var baseRevision = repository.Git("rev-parse", "HEAD").Trim();
        repository.WriteFile("one.txt", "one\n").CommitAs("feat(core): add first change");
        repository.WriteFile("two.txt", "two\n").CommitAs("fixup! feat(core): add first change");

        var run = HarnessCli.Run(repository.Path, "commits", "check", $"{baseRevision}..HEAD");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("must be resolved", run.StandardError, StringComparison.Ordinal);
    }

    private static RepositoryFixture Repository(bool RequireSetup, string language)
        => Fixtures.Compliant(Frame.AllPresent().Settings(
            $$"""{ "commits": { "language": "{{language}}", "requireSetup": {{RequireSetup.ToString().ToLowerInvariant()}} } }"""));
}
