namespace Harness.Tests;

public sealed class WorkspaceAnsibleTests
{
    [Theory]
    [InlineData("ansible.cfg")]
    [InlineData("deploy/ansible.cfg")]
    public void Ancestor_or_sibling_markers_do_not_make_plain_yaml_an_ansible_project(string marker)
    {
        using var repository = Workspace()
            .WriteFile(marker, "[defaults]\n")
            .WriteFile("app/values.yml", "command: echo # noqa\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "app", "--only", "lint-suppressions.ansible");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("no Ansible marker in the index", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("silences every ansible-lint rule", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_coverage_excludes_declared_project_ansible_sources()
    {
        using var repository = Workspace()
            .WriteFile(".harness.json", """
                {"version":"latest","projects":["app","deploy"],
                 "settings":{"commits":{"language":"ru","requireSetup":false}},
                 "policy":{"harness.coverage":"required"}}
                """)
            .WriteFile("app/ansible.cfg", "[defaults]\n")
            .WriteFile("app/roles/web/tasks/main.yml", "- name: Ping\n  ansible.builtin.ping:\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("but no `applicability.ansible` entry", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("but no `applicability.yaml` entry", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("off", 0)]
    [InlineData("advisory", 0)]
    [InlineData("required", 1)]
    public void Project_ansible_policy_and_cached_sources_stay_local(string policy, int expected)
    {
        using var repository = Workspace()
            .WriteFile("app/roles/web/tasks/main.yml", "- name: Ping\n  ansible.builtin.ping:\n")
            .WriteFile("deploy/.harness.json", ProjectFrame(policy))
            .WriteFile("deploy/roles/web/tasks/main.yml", "- name: Include self\n  ansible.builtin.include_role:\n    name: web\n")
            .Commit();

        var selected = HarnessCli.RunVerbose(repository.Path, "check", "--project", "app", "--only", "dependencies.ansible");
        Assert.Equal(0, selected.ExitCode);
        Assert.DoesNotContain("role dependency cycle", selected.Output, StringComparison.Ordinal);

        var full = HarnessCli.RunVerbose(repository.Path, "check", "--only", "dependencies.ansible");
        Assert.Equal(expected, full.ExitCode);
        if (policy != "off")
        {
            Assert.Contains("role dependency cycle web -> web", full.Output, StringComparison.Ordinal);
        }
    }

    private static RepositoryFixture Workspace()
        => Fixtures.WithRawFrame("""
            {"version":"latest","projects":["app","deploy"],
             "settings":{"commits":{"language":"ru","requireSetup":false}},"policy":{}}
            """)
            .WriteFile("app/.harness.json", ProjectFrame("required"))
            .WriteFile("deploy/.harness.json", """{"settings":{},"policy":{}}""");

    private static string ProjectFrame(string policy)
        => $$$"""
            {"settings":{},"applicability":{"ansible":{"applicable":true},"yaml":{"applicable":true}},
             "policy":{"harness.coverage":"required","lint-suppressions.ansible":"{{{policy}}}","dependencies.ansible":"{{{policy}}}"}}
            """;
}
