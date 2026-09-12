namespace Harness.Checks.Ansible;

internal static class RoleShapeExplanation
{
    public const string Text =
        """
        Rationale
          A role is applied by name and read by convention: Ansible opens `tasks/main.yml`,
          `defaults/main.yml`, `handlers/main.yml` and the other directories of its vocabulary,
          and never looks anywhere else. A file dropped next to them — a stray playbook, a
          note, a `main.yml` in the role root — is silently ignored, which is the worst way for
          it to be wrong. This is the Ansible counterpart of `zone-shape` for sliced-dotnet.

        What it reads
          Every tracked path under `roles/<name>/` outside generated, vendored and build-output
          locations, once the repository carries an Ansible marker. No file content is read.

        Rule
          Directly below `roles/<name>/` there are only the directories `tasks`, `defaults`,
          `vars`, `handlers`, `templates`, `files`, `meta`, `library`, `module_utils`,
          `filter_plugins`, `lookup_plugins`, `test_plugins`, `action_plugins`, `tests`,
          `molecule`, and the file `README.md`. A role with a `tasks/` directory has
          `tasks/main.yml` (or `main.yaml`). A YAML file directly in the role directory is a
          violation of the first rule.

        Limits
          Nested role layouts of a collection (`roles/<group>/<name>/`) read as one role named
          by the first segment and report its directories as unknown. `harness init` starts this
          check as required despite this known limitation. Directory names are compared
          exactly. Whether `main.yml` is valid YAML belongs to `ansible-playbook --syntax-check`
          through `answers.build`.

        Remediation
          Move a stray file under the directory Ansible reads it from, or out of the role. Add
          `tasks/main.yml` as the entry point of a role that has tasks. A repository with a
          deliberate non-standard layout discusses advisory or off with the repository owner.

        Applicability
          Disable every Ansible check together only when Ansible does not apply:

          "applicability": {
            "ansible": { "applicable": false, "reason": "why Ansible checks do not apply" }
          }

        Decisions
          adrs/0057-ansible-axis.md
        """;
}
