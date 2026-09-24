using System.Text.Json;

namespace Harness.Tests;

/// <summary>
/// The Ansible axis of contract 3.2 (ADR-0057): detected by markers, two checks over tracked
/// YAML files, each read through the compiled CLI on a fixture repository.
/// </summary>
public sealed class AnsibleAxisTests
{
    private const string LintSuppressions = "lint-suppressions.ansible";

    private const string Dependencies = "dependencies.ansible";

    private static readonly string[] Checks = [LintSuppressions, Dependencies];

    [Fact]
    public void Yaml_without_an_ansible_marker_is_not_applicable_to_every_ansible_check()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("deploy/values.yml", "image: nginx:latest\ndb_password: correct-horse-1\n")
            .WriteFile("roles/web/main.yml", "- name: stray\n")
            .Commit();

        foreach (var check in Checks)
        {
            var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", check);

            Assert.Equal(0, run.ExitCode);
            Assert.True(run.OutputContains("not applicable"), run.Output);
            Assert.True(run.OutputContains("no Ansible marker in the index"), run.Output);
        }
    }

    [Theory]
    [InlineData("ansible.cfg", "[defaults]\ninventory = inventory.ini\n")]
    [InlineData("roles/common/tasks/main.yml", "- name: Ping\n  ansible.builtin.ping:\n")]
    [InlineData("playbooks/site.yml", "- name: Site\n  hosts: all\n")]
    [InlineData("site.yml", "---\n- name: Site\n  hosts: all\n  roles: []\n")]
    public void Each_marker_alone_detects_the_axis(string path, string content)
    {
        using var repository = RepositoryFixture.CreateGitRepository().WriteFile(path, content).Commit();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Declared", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ansible from the tracked sources", run.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_yaml_without_hosts_and_a_marker_under_a_dot_directory_do_not_detect_the_axis()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("docker-compose.yml", "services:\n  web:\n    image: nginx\n")
            .WriteFile("values.yml", "- name: not a play\n  tasks: []\n")
            .WriteFile(".venv-lint/roles/x/tasks/main.yml", "- name: vendored\n")
            .Commit();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Declared yaml from the tracked sources", run.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Noqa_without_rules_or_without_a_reason_is_blocking_and_the_config_lists_are_details()
    {
        const string tasks = "- name: Bare\n  ansible.builtin.shell: echo # noqa\n"
            + "- name: No reason\n  ansible.builtin.shell: echo # noqa: fqcn[action] command-instead-of-shell\n"
            + "- name: Space form\n  ansible.builtin.shell: echo # noqa no-changed-when\n"
            + "- name: Reasoned\n  ansible.builtin.shell: echo # noqa: no-changed-when -- read-only query\n"
            + "- name: Second hash\n  ansible.builtin.shell: echo # noqa command-instead-of-shell # pipes\n"
            + "# noqa is discussed in prose here\n";
        using var repository = Ansible(Frame.AllPresent().Settings(
                """{ "lint-suppressions.ansible": { "repositoryWide": [ { "id": "name[play]", "reason": "fixture" }, { "id": "yaml[line-length]", "reason": "fixture" } ] } }"""))
            .WriteFile("roles/web/tasks/main.yml", tasks)
            .WriteFile(".ansible-lint", "---\nskip_list:\n  - name[play]   # import_playbook has no name\nwarn_list:\n  - yaml[line-length]\nexclude_paths:\n  - .venv/\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", LintSuppressions);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:2: silences every ansible-lint rule via a bare `# noqa`"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:4: silences fqcn[action], command-instead-of-shell at one address via `# noqa`"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:6: silences no-changed-when at one address via"), run.Output);
        Assert.True(run.OutputContains("main.yml:8"), run.Output);
        Assert.True(run.OutputContains("main.yml:10"), run.Output);
        Assert.False(run.OutputContains("main.yml:11"), run.Output);
        Assert.True(run.OutputContains("name[play] is skipped repository-wide via skip_list at .ansible-lint:2"), run.Output);
        Assert.True(run.OutputContains("yaml[line-length] is downgraded to a warning repository-wide via warn_list at .ansible-lint:4"), run.Output);
        Assert.True(run.OutputContains(".venv/"), run.Output);
    }

    [Fact]
    public void Two_roles_including_each_other_are_one_proved_cycle()
    {
        using var repository = Ansible()
            .WriteFile("roles/web/tasks/main.yml", "- name: Need the proxy\n  ansible.builtin.include_role:\n    name: proxy\n")
            .WriteFile("roles/proxy/meta/main.yml", "galaxy_info:\n  author: fixture\ndependencies:\n  - role: web\n  - community.general.some_role\n")
            .WriteFile("roles/proxy/tasks/main.yml", "- name: Dynamic\n  ansible.builtin.import_role:\n    name: \"{{ dynamic_role }}\"\n")
            .WriteFile("roles/db/handlers/main.yml", "- name: Restart\n  ansible.builtin.include_role:\n    name: common\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Dependencies);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("role dependency cycle proxy -> web -> proxy"), run.Output);
        Assert.True(run.OutputContains("proxy names web at roles/proxy/meta/main.yml:4"), run.Output);
        Assert.True(run.OutputContains("web names proxy at roles/web/tasks/main.yml:3"), run.Output);
        Assert.Equal(1, Occurrences(run.Output, "role dependency cycle"));
        Assert.True(run.OutputContains("75% of the 4 role names"), run.Output);
    }

    [Fact]
    public void An_acyclic_role_graph_passes_and_a_playbook_is_not_a_node()
    {
        using var repository = Ansible()
            .WriteFile("roles/web/meta/main.yml", "dependencies:\n- common\n")
            .WriteFile("site.yml", "- name: Site\n  hosts: all\n  roles:\n    - web\n  tasks:\n    - ansible.builtin.include_role:\n        name: common\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Dependencies);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("outcome: passed"), run.Output);
        Assert.True(run.OutputContains("100% of the 1 role names"), run.Output);
    }

    [Fact]
    public void Init_on_an_ansible_repository_writes_both_axes_the_required_defaults_and_the_toolchain_hint()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("ansible.cfg", "[defaults]\n")
            .WriteFile("site.yml", "- name: Site\n  hosts: all\n")
            .Commit();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Declared yaml, ansible from the tracked sources", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("`ansible-lint`", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("`ansible-playbook --syntax-check`", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("molecule", run.StandardOutput, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        var root = document.RootElement;
        Assert.Equal(["yaml", "ansible"], root.GetProperty("applicability").EnumerateObject().Select(axis => axis.Name));
        Assert.Equal(["adrs.shape", "comments.yaml", "commits"], root.GetProperty("settings").EnumerateObject().Select(section => section.Name));
        var policy = root.GetProperty("policy");
        Assert.Equal("required", policy.GetProperty("comments.yaml").GetString());
        Assert.Equal("required", policy.GetProperty(LintSuppressions).GetString());
        Assert.Equal("required", policy.GetProperty(Dependencies).GetString());

        repository.CommitAs("chore(harness): инициализировать рамку репозитория");
        var check = HarnessCli.Run(repository.Path, "check", "--skip", "frame,docs,commits");
        Assert.Equal(0, check.ExitCode);
        Assert.False(check.OutputContains("not applicable"), check.Output);
    }

    [Fact]
    public void Coverage_prints_the_ansible_fragment_for_an_undeclared_ansible_axis()
    {
        const string frame = """
            {
              "version": "latest",
              "applicability": { "yaml": { "applicable": true } },
              "settings": { "commits": { "language": "ru", "requireSetup": false } },
              "policy": { "harness.coverage": "required" }
            }
            """;
        using var repository = Fixtures.WithRawFrame(frame)
            .WriteFile("ansible.cfg", "[defaults]\n")
            .WriteFile("roles/common/tasks/main.yml", "- name: Ping\n  ansible.builtin.ping:\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("2 tracked Ansible sources (ansible.cfg, roles/common/tasks/main.yml) but no `applicability.ansible` entry"), run.Output);
        Assert.True(run.OutputContains("\"ansible\": { \"applicable\": true }"), run.Output);
        Assert.True(run.OutputContains("\"dependencies.ansible\": \"required\""), run.Output);
        Assert.True(run.OutputContains("\"lint-suppressions.ansible\": \"required\""), run.Output);
        Assert.False(run.OutputContains("\"settings\""), run.Output);
    }

    [Theory]
    [InlineData("images.ansible")]
    [InlineData("secrets.ansible")]
    [InlineData("role-shape.ansible")]
    public void Deferred_checks_are_not_advertised_or_initialized(string id)
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("ansible.cfg", "[defaults]\n").Commit();
        var help = HarnessCli.Run(repository.Path, "help");
        Assert.Equal(0, help.ExitCode);
        Assert.False(help.OutputContains(id), help.Output);
        Assert.Equal(0, HarnessCli.RunWithInput(repository.Path, string.Empty, "init").ExitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.False(json.RootElement.GetProperty("policy").TryGetProperty(id, out _));
    }

    [Fact]
    public void Explain_describes_each_ansible_check_in_its_own_terms()
    {
        using var repository = Fixtures.Compliant();

        var suppressions = HarnessCli.Run(repository.Path, "explain", LintSuppressions);
        var dependencies = HarnessCli.Run(repository.Path, "explain", Dependencies);

        Assert.Equal(0, suppressions.ExitCode);
        Assert.True(suppressions.OutputContains("skip_list") && suppressions.OutputContains(".ansible-lint"), suppressions.Output);
        Assert.Equal(0, dependencies.ExitCode);
        Assert.True(dependencies.OutputContains("include_role") && dependencies.OutputContains("Playbooks compose roles"), dependencies.Output);
    }

    [Fact]
    public void Frame_explanations_and_the_go_hint_mention_the_ansible_toolchain()
    {
        using var repository = Fixtures.Compliant();

        var lint = HarnessCli.Run(repository.Path, "explain", "frame.lint");
        var build = HarnessCli.Run(repository.Path, "explain", "frame.build");
        var integration = HarnessCli.Run(repository.Path, "explain", "frame.tests.integration");

        Assert.True(lint.OutputContains("`ansible-lint` and `yamllint`"), lint.Output);
        Assert.True(build.OutputContains("`ansible-playbook --syntax-check`"), build.Output);
        Assert.True(integration.OutputContains("molecule"), integration.Output);
    }

    [Fact]
    public void A_role_including_itself_is_a_cycle()
    {
        using var repository = Ansible()
            .WriteFile("roles/web/tasks/main.yml", "- ansible.builtin.include_role:\n    name: web\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Dependencies);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("role dependency cycle web -> web"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:2"), run.Output);
    }

    [Fact]
    public void Role_names_with_dots_are_distinct_and_nested_tags_are_not_dependencies()
    {
        using var repository = Ansible()
            .WriteFile("roles/web/meta/main.yml", "dependencies:\n  - role: common\n    tags:\n      - web.child\n")
            .WriteFile("roles/web.child/tasks/main.yml", "- ansible.builtin.include_role:\n    name: web\n")
            .Commit();

        Assert.Equal(0, HarnessCli.Run(repository.Path, "check", "--only", Dependencies).ExitCode);
        repository.WriteFile("roles/web/meta/main.yml", "dependencies:\n  - web.child\n").Commit();
        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Dependencies);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("role dependency cycle web -> web.child -> web"), run.Output);
    }

    [Theory]
    [InlineData("- name: Task\n  vars:\n    hosts: all\n")]
    [InlineData("- name: Task\n  content: |\n    hosts: all\n")]
    [InlineData("- name: Task\n  content: |\n    - hosts: all\n")]
    public void Nested_hosts_does_not_turn_a_task_file_into_a_playbook(string content)
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("tasks.yml", content).Commit();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init");

        Assert.Equal(0, run.ExitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.False(json.RootElement.GetProperty("applicability").TryGetProperty("ansible", out _));
    }

    [Theory]
    [InlineData("required")]
    [InlineData("advisory")]
    [InlineData("off")]
    public void Upgrade_from_31_preserves_policy_and_prints_the_new_axis(string policy)
    {
        var frame = """
            {
              "version": "3.1.0",
              "applicability": { "yaml": { "applicable": true } },
              "settings": { "comments.yaml": { "percentageLimit": 8, "minimumCommentLines": 10 } },
              "policy": { "comments.yaml": "required", "harness.coverage": "required" }
            }
            """;
        frame = frame.Replace("\"comments.yaml\": \"required\"", $"\"comments.yaml\": \"{policy}\"", StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame)
            .WriteFile("ansible.cfg", "[defaults]\n").Commit();

        var dry = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");
        Assert.Equal(0, dry.ExitCode);
        Assert.True(dry.OutputContains("Release 3.2 additions"), dry.Output);
        Assert.Equal(frame, File.ReadAllText(repository.Absolute(".harness.json")));
        var run = HarnessCli.Run(repository.Path, "upgrade");
        Assert.Equal(0, run.ExitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal(Release.Current, json.RootElement.GetProperty("version").GetString());
        Assert.Equal(policy, json.RootElement.GetProperty("policy").GetProperty("comments.yaml").GetString());
        Assert.False(json.RootElement.GetProperty("applicability").TryGetProperty("ansible", out _));
    }

    /// <summary>A compliant fixture with the Ansible markers: ansible.cfg, a root playbook and one role.</summary>
    private static RepositoryFixture Ansible(Frame? frame = null)
        => Fixtures.Compliant(frame ?? Frame.AllPresent())
            .WriteFile("ansible.cfg", "[defaults]\ninventory = inventory.ini\nroles_path = roles\n")
            .WriteFile("site.yml", "---\n- name: Site\n  hosts: all\n  become: true\n  roles:\n    - common\n")
            .WriteFile("roles/common/tasks/main.yml", "---\n- name: Ping\n  ansible.builtin.ping:\n");

    private static int Occurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;
}
