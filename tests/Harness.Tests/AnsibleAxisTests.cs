using System.Text.Json;

namespace Harness.Tests;

/// <summary>
/// The Ansible axis of contract 3.2 (ADR-0057): detected by markers, five checks over tracked
/// YAML and Jinja templates, each read through the compiled CLI on a fixture repository.
/// </summary>
public sealed class AnsibleAxisTests
{
    private const string Images = "images.ansible";

    private const string Secrets = "secrets.ansible";

    private const string LintSuppressions = "lint-suppressions.ansible";

    private const string Dependencies = "dependencies.ansible";

    private const string RoleShape = "role-shape.ansible";

    private static readonly string[] Checks = [Images, Secrets, LintSuppressions, Dependencies, RoleShape];

    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

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
    public void Every_image_form_without_a_full_digest_is_blocking_and_jinja_is_a_detail()
    {
        var tasks = "- name: Run\n  community.docker.docker_container:\n"
            + "    image: nginx:latest\n"
            + "- name: Tag\n  vars:\n    image: nginx:1.27   # pinned by tag\n"
            + "- name: Garbage\n  vars:\n    image: \"nginx@sha256:garbage\"\n"
            + "- name: Short\n  vars:\n    image: nginx@sha256:0123456789abcdef\n"
            + "- name: Empty\n  vars:\n    image:\n"
            + $"- name: Good\n  vars:\n    image: \"registry.example/app@sha256:{Digest}\"   # 1.2.3\n";
        using var repository = Ansible()
            .WriteFile("roles/web/tasks/main.yml", tasks)
            .WriteFile("roles/web/templates/compose.yml.j2", "services:\n  web:\n    image: {{ web_image }}\n    ports: []\n")
            .WriteFile("molecule/default/converge.yml", "- hosts: all\n  tasks:\n    - vars:\n        image: nginx:latest\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Images);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:3") && run.OutputContains("`nginx:latest` is named by tag, not by digest"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:6"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:9: image `nginx@sha256:garbage` carries an incomplete digest"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:12: image `nginx@sha256:0123456789abcdef` carries an incomplete digest"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:15: image has no value"), run.Output);
        Assert.False(run.OutputContains("main.yml:18"), run.Output);
        Assert.True(run.OutputContains("roles/web/templates/compose.yml.j2:3: image `{{ web_image }}` is resolved by Ansible at run time (Inferred)"), run.Output);
        Assert.False(run.OutputContains("molecule/"), run.Output);
    }

    [Fact]
    public void Anchors_aliases_and_flow_collections_do_not_become_literal_findings()
    {
        using var repository = Ansible()
            .WriteFile("group_vars/all/values.yml", "image: *shared_image\ndb_password: *shared_password\n")
            .WriteFile("roles/web/defaults/main.yml", "image: &shared_image nginx:latest\n")
            .WriteFile("roles/web/templates/config.yml.j2", "image: {name: nginx}\n")
            .Commit();

        var images = HarnessCli.RunVerbose(repository.Path, "check", "--only", Images);
        var secrets = HarnessCli.RunVerbose(repository.Path, "check", "--only", Secrets);

        Assert.Equal(0, images.ExitCode);
        Assert.Equal(3, Occurrences(images.Output, "anchor, alias or flow collection (Inferred)"));
        Assert.Equal(0, secrets.ExitCode);
    }

    [Fact]
    public void Images_pinned_by_digest_pass()
    {
        using var repository = Ansible()
            .WriteFile("group_vars/all/services.yml", $"services:\n  web:\n    image: \"registry.example/web@sha256:{Digest}\"   # 2.6.2\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Images);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("every literal image is pinned by a full digest"), run.Output);
    }

    [Fact]
    public void A_literal_under_a_secret_name_in_a_variables_file_is_the_only_secret_finding()
    {
        const string vars = "wg_public_key: \"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"\n"
            + "token_key: token\n"
            + "vault_password: !vault |\n  $ANSIBLE_VAULT;1.1;AES256\n  62313365396662343061393464336163383764373764613633653634306231386433626436623361\n"
            + "api_key: \"{{ lookup('env', 'API_KEY') }}\"\n"
            + "ssh_key_file: ~/.ssh/id_ed25519\n"
            + "token_ttl: 15m\n"
            + "managed_keys: [owner_password]\n"
            + "env:\n  MASTER_PASSWORD: master_password\n"
            + "db_password: hunter2\n"
            + "admin_password: correct-horse-1   # generated once\n"
            + "client_secret: ~\n";
        using var repository = Ansible()
            .WriteFile("group_vars/all/access.yml", vars)
            .WriteFile("roles/web/defaults/main.yml", "web_api_key: \"{{ vault_web_api_key }}\"\n")
            .WriteFile("roles/web/tasks/main.yml", "- name: Set\n  ansible.builtin.set_fact:\n    password: literal-outside-scope\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Secrets);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("violation  group_vars/all/access.yml:13: `admin_password` holds a literal value in a variables file"), run.Output);
        Assert.False(run.OutputContains("access.yml:1:"), run.Output);
        Assert.Equal(1, Occurrences(run.Output, "holds a literal value"));
        Assert.False(run.OutputContains("roles/web/tasks/main.yml"), run.Output);
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
        using var repository = Ansible()
            .WriteFile("roles/web/tasks/main.yml", tasks)
            .WriteFile(".ansible-lint", "---\nskip_list:\n  - name[play]   # import_playbook has no name\nwarn_list:\n  - yaml[line-length]\nexclude_paths:\n  - .venv/\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", LintSuppressions);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:2: silences every ansible-lint rule via a bare `# noqa`"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:4: silences fqcn[action], command-instead-of-shell via `# noqa: fqcn[action] command-instead-of-shell` without a reason"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks/main.yml:6: silences no-changed-when via"), run.Output);
        Assert.False(run.OutputContains("main.yml:8"), run.Output);
        Assert.False(run.OutputContains("main.yml:10"), run.Output);
        Assert.False(run.OutputContains("main.yml:11"), run.Output);
        Assert.True(run.OutputContains("name[play] is skipped repository-wide via skip_list at .ansible-lint:2"), run.Output);
        Assert.True(run.OutputContains("yaml[line-length] is downgraded to a warning repository-wide via warn_list at .ansible-lint:4"), run.Output);
        Assert.False(run.OutputContains(".venv/"), run.Output);
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
    public void A_role_with_a_stray_file_an_unknown_directory_or_no_tasks_main_is_reported()
    {
        using var repository = Ansible()
            .WriteFile("roles/web/main.yml", "- name: stray\n")
            .WriteFile("roles/web/scripts/run.sh", "#!/bin/sh\n")
            .WriteFile("roles/web/tasks/install.yml", "- name: Install\n  ansible.builtin.ping:\n")
            .WriteFile("roles/web/README.md", "# web\n")
            .WriteFile("roles/db/tasks/main.yaml", "- name: Ping\n  ansible.builtin.ping:\n")
            .WriteFile("roles/db/files/schema.sql", "select 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", RoleShape);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("roles/web/main.yml: a file directly in the role directory"), run.Output);
        Assert.True(run.OutputContains("roles/web/scripts: not a directory Ansible reads from a role"), run.Output);
        Assert.True(run.OutputContains("roles/web/tasks: has no main.yml"), run.Output);
        Assert.False(run.OutputContains("roles/db"), run.Output);
        Assert.False(run.OutputContains("README.md:"), run.Output);
    }

    [Fact]
    public void Init_on_an_ansible_repository_writes_both_axes_the_advisory_defaults_and_the_toolchain_hint()
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
        Assert.Equal(["comments.yaml", "commits"], root.GetProperty("settings").EnumerateObject().Select(section => section.Name));
        var policy = root.GetProperty("policy");
        Assert.Equal("advisory", policy.GetProperty("comments.yaml").GetString());
        Assert.Equal("required", policy.GetProperty(Images).GetString());
        Assert.Equal("advisory", policy.GetProperty(Secrets).GetString());
        Assert.Equal("required", policy.GetProperty(LintSuppressions).GetString());
        Assert.Equal("required", policy.GetProperty(Dependencies).GetString());
        Assert.Equal("advisory", policy.GetProperty(RoleShape).GetString());

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
        Assert.True(run.OutputContains("\"images.ansible\": \"required\""), run.Output);
        Assert.True(run.OutputContains("\"secrets.ansible\": \"advisory\""), run.Output);
        Assert.False(run.OutputContains("\"settings\""), run.Output);
    }

    [Fact]
    public void Explain_describes_each_ansible_check_in_its_own_terms()
    {
        using var repository = Fixtures.Compliant();

        var images = HarnessCli.Run(repository.Path, "explain", Images);
        var secrets = HarnessCli.Run(repository.Path, "explain", Secrets);
        var suppressions = HarnessCli.Run(repository.Path, "explain", LintSuppressions);
        var dependencies = HarnessCli.Run(repository.Path, "explain", Dependencies);
        var shape = HarnessCli.Run(repository.Path, "explain", RoleShape);

        Assert.Equal(0, images.ExitCode);
        Assert.True(images.OutputContains("64 hexadecimal digits") && images.OutputContains("*.j2"), images.Output);
        Assert.Equal(0, secrets.ExitCode);
        Assert.True(secrets.OutputContains("public_key") && secrets.OutputContains("token_key: token"), secrets.Output);
        Assert.Equal(0, suppressions.ExitCode);
        Assert.True(suppressions.OutputContains("skip_list") && suppressions.OutputContains(".ansible-lint"), suppressions.Output);
        Assert.Equal(0, dependencies.ExitCode);
        Assert.True(dependencies.OutputContains("include_role") && dependencies.OutputContains("Playbooks compose roles"), dependencies.Output);
        Assert.Equal(0, shape.ExitCode);
        Assert.True(shape.OutputContains("tasks/main.yml") && shape.OutputContains("zone-shape"), shape.Output);
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

    [Fact]
    public void Upgrade_from_31_preserves_policy_and_prints_the_new_axis()
    {
        const string frame = """
            {
              "version": "3.1.0",
              "applicability": { "yaml": { "applicable": true } },
              "settings": { "comments.yaml": { "percentageLimit": 8, "minimumCommentLines": 10 } },
              "policy": { "comments.yaml": "required", "harness.coverage": "required" }
            }
            """;
        using var repository = Fixtures.WithRawFrame(frame)
            .WriteFile("ansible.cfg", "[defaults]\n").Commit();

        var dry = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");
        Assert.Equal(0, dry.ExitCode);
        Assert.True(dry.OutputContains("Release 3.2 additions"), dry.Output);
        Assert.True(dry.OutputContains("\"secrets.ansible\": \"advisory\""), dry.Output);
        Assert.Equal(frame, File.ReadAllText(repository.Absolute(".harness.json")));
        var run = HarnessCli.Run(repository.Path, "upgrade");
        Assert.Equal(0, run.ExitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal(Release.Current, json.RootElement.GetProperty("version").GetString());
        Assert.Equal("required", json.RootElement.GetProperty("policy").GetProperty("comments.yaml").GetString());
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
