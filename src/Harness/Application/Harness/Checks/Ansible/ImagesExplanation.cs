namespace Harness.Checks.Ansible;

internal static class ImagesExplanation
{
    public const string Text =
        """
        Rationale
          A container image named by tag — `nginx:1.27`, `nginx:latest`, or no tag at all — can
          change under a deployment: the registry serves whatever the tag points at today. An
          image named by its digest is immutable, and a repository that deploys it can say what
          it deployed. compose-lint and checkov check compose files; they do not read the Jinja
          templates an Ansible role renders them from, so the harness does.

        What it reads
          Tracked `.yml`, `.yaml` and `.j2` files outside generated, vendored and build-output
          locations, directories starting with `.` and `molecule/`, once the repository carries
          an Ansible marker (ansible.cfg at the root, roles/<name>/tasks/main.yml, playbooks/*.yml
          or a root playbook with `hosts:`). The reading is lexical: every line whose mapping key
          is `image` and whose value is a scalar, after quotes and a trailing comment are removed.

        Rule
          The value ends with `@sha256:` followed by exactly 64 hexadecimal digits. Anything else
          is blocking: a bare name, a tag, `:latest`, an empty value, or `@sha256:` with a short
          or non-hexadecimal digest.

        What is printed instead
          A value written in Jinja (`image: {{ service.image }}`) is resolved by Ansible at run
          time. It is printed as a detail with --verbose, graded Inferred, and never blocks; the
          variable is not traced. Only declarations under a literal `image` key are checked.

        Limits
          Anchors, aliases and flow collections are Inferred, not findings.
          The variable is not traced by name: an image assembled in `set_fact` or supplied from
          inventory is Inferred here and nothing more. A block scalar folds into the value of the
          key that opened it, so an `image:` line inside inline compose under `content: |` is not
          judged. The digest is not verified against a registry.

        Remediation
          Resolve the digest once — `docker manifest inspect` or `skopeo inspect` — and write
          `image: name@sha256:<digest>`, keeping the tag as a trailing comment for readers. To
          accept tags knowingly for the whole repository, set `policy.images.ansible` to
          advisory or off; there is no per-file exception.

        Applicability
          Disable every Ansible check together only when Ansible does not apply:

          "applicability": {
            "ansible": { "applicable": false, "reason": "why Ansible checks do not apply" }
          }

        Decisions
          adrs/0057-ansible-axis.md
        """;
}
