namespace Harness.Checks.Ansible;

internal static class SecretsExplanation
{
    public const string Text =
        """
        Rationale
          Ansible has a form for a secret in a variables file: a `!vault` block, or a lookup that
          fetches it at run time. A literal under `db_password:` is the one form that leaks the
          moment the repository is cloned. gitleaks looks for content — entropy and provider
          patterns — through `answers.*`; this check asks for the form under names that obviously
          hold a secret, and asks nothing about names it does not know.

        What it reads
          Tracked YAML under `group_vars/`, `host_vars/`, `vars/`, `roles/<name>/defaults/`,
          `roles/<name>/vars/` and `inventory*`, once the repository carries an Ansible marker.
          Every mapping key, at any depth, is compared with the dictionary case-insensitively —
          by the whole name or by a run of its `_`/`-` segments: `password`, `passwd`, `secret`,
          `secret_id`, `token`, `api_key`, `apikey`, `access_key`, `private_key`, `privatekey`,
          `client_secret`. Names that only sound like one are excluded: `public_key`, `pubkey`,
          `*_key_file`, `*_key_path`, `*_key_name`, `*_keys`, `token_file`, `token_ttl`, `*_ttl`;
          a `*_key` without a dictionary prefix is not a secret.

        Rule
          Under a dictionary name, a value is a finding when it is a literal: at least 8
          characters, not a `!vault` block, not a Jinja expression (`{{ ... }}`, which is where
          lookups live), not empty, `null` or `~`, not a flow collection, and not a snake_case
          identifier such as `master_password` — that shape names a field in a store, it is not
          the field's value. `token_key: token` passes on both counts.

        Limits
          The dictionary is a table in the code: `db_pw` is not judged, and that is accepted —
          the check requires a form for obvious names, it does not find secrets. A password
          made of lowercase words and underscores reads as an identifier. A `!vault` block inside
          a flow mapping reads as absent. Whether a task logs the value (`no_log`) belongs to
          ansible-lint's no-log-password rule, not here.

        Remediation
          Encrypt the value with `ansible-vault encrypt_string` and paste the `!vault` block, or
          replace the literal with a lookup against the secret store. A test value in
          `defaults/` is vaulted or left empty the same way. `harness init` writes this check as
          required. Discuss a policy change to advisory or off with the repository owner.

        Applicability
          Disable every Ansible check together only when Ansible does not apply:

          "applicability": {
            "ansible": { "applicable": false, "reason": "why Ansible checks do not apply" }
          }

        Decisions
          adrs/0057-ansible-axis.md
        """;
}
