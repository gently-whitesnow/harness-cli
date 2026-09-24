namespace Harness.Checks.Ansible;

internal static class AnsibleLintSuppressionsExplanation
{
    public const string Text =
        """
        Rationale
          ansible-lint holds only while nobody silences it one task at a time. Suppressions
          accumulate quietly and each looks reasonable in its own diff. The harness applies its
          own policy rule (ADR-0035) to the linter's findings: a rule is either on for the
          repository or off for it, and no task gets a private exception.

        What it reads
          Tracked `.yml` and `.yaml` files, excluding only toolchain-ignored directories
          starting with `.` and `molecule/`, once the repository carries
          an Ansible marker — and a tracked `.ansible-lint`, `.ansible-lint.yml` or
          `.ansible-lint.yaml`, read lexically by indentation and `- ` items. No linter is
          located, installed or run, and no report is read.

        What fails
          Every inline `# noqa` is an address-level suppression and blocks, even when it
          names a rule and gives a reason. `exclude_paths` also blocks.

        What is printed instead
          A rule listed under `skip_list` or `warn_list` in the configuration is switched off or
          downgraded for the whole repository: the tracked, reviewable decision `policy: off` is
          for harness checks only when settings.lint-suppressions.ansible.repositoryWide
          contains its id and reason. Every switch is printed in the ordinary report.

        Limits
          The configuration reader knows the documented shape; an anchor, a flow mapping or a
          profile inherited from a collection may read as absent. A `# noqa` inside a Jinja template
          is not a directive to ansible-lint and is not one here.

        Remediation
          Fix the task the rule points at. When the rule is wrong for this repository as a
          whole, add it to `skip_list` and declare its id and reason in `.harness.json`. If the repository rejects this check entirely, record that
          through `policy.lint-suppressions.ansible`.

        Applicability
          Disable every Ansible check together only when Ansible does not apply:

          "applicability": {
            "ansible": { "applicable": false, "reason": "why Ansible checks do not apply" }
          }

        Decisions
          adrs/0055-go-language-axis.md
          adrs/0057-ansible-axis.md
        """;
}
