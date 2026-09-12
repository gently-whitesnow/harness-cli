namespace Harness.Checks.Ansible;

internal static class AnsibleLintSuppressionsExplanation
{
    public const string Text =
        """
        Rationale
          ansible-lint holds only while nobody silences it one task at a time. Suppressions
          accumulate quietly and each looks reasonable in its own diff. The harness applies its
          own policy rule (ADR-0035) to the linter's findings: a rule is either on for the
          repository or off for it, and no task gets a private exception without a named rule
          and a written reason. ansible-lint does not ask for the reason; the harness does.

        What it reads
          Tracked `.yml` and `.yaml` files outside generated, vendored and build-output
          locations, directories starting with `.` and `molecule/`, once the repository carries
          an Ansible marker — and a tracked `.ansible-lint`, `.ansible-lint.yml` or
          `.ansible-lint.yaml`, read lexically by indentation and `- ` items. No linter is
          located, installed or run, and no report is read.

        What fails
          A `# noqa` comment that does not say what it silences and why: a bare `# noqa`
          silences every rule and is always blocking; `# noqa: fqcn[action]` or
          `# noqa fqcn[action]` without a reason on the same line is blocking.
          `# noqa: fqcn[action] -- the module ships with the collection` passes, as does a
          reason after a second `#` on the same line.

        What is printed instead
          A rule listed under `skip_list` or `warn_list` in the configuration is switched off or
          downgraded for the whole repository: the tracked, reviewable decision `policy: off` is
          for harness checks. Every such rule is a neutral detail with --verbose and never fails
          the run.

        Limits
          The configuration reader knows the documented shape; an anchor, a flow mapping or a
          profile inherited from a collection reads as absent. `exclude_paths` is not read: it
          removes places from the linter's inventory rather than silencing a rule at them, and
          the harness has no view of what those places hold. A `# noqa` inside a Jinja template
          is not a directive to ansible-lint and is not one here.

        Remediation
          Fix the task the rule points at. When the rule is wrong at one place, keep the
          directive but make it say what and why: `# noqa: no-changed-when -- read-only query`.
          When the rule is wrong for this repository as a whole, add it to `skip_list` and say
          why in the same file. If the repository rejects this check entirely, record that
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
