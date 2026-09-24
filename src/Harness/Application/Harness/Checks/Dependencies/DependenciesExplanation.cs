using Harness.Languages;

namespace Harness.Checks.Dependencies;

/// <summary>Long-form content for `harness explain dependencies.csharp` and `dependencies.ansible`.</summary>
internal static class DependenciesExplanation
{
    public static string For(Language language) => language == Language.Ansible ? Ansible
        : language == Language.TypeScript ? TypeScript : Text;

    private const string TypeScript =
        """
        Rationale
          TypeScript and JavaScript allow import cycles. A cycle across directories couples
          modules that should have one dependency direction.

        Discovery
          Tracked .ts, .tsx, .mts, .cts, .js, .jsx, .mjs and .cjs source is read without
          Node or tsc. Tests and stories do not contribute graph edges.

        Resolution
          Literal import, export from, require and import() specifiers resolve through
          relative extension probing, tracked tsconfig paths/baseUrl and package manifests.
          Type-only imports still contribute edges. External dependencies and assets are
          not nodes. Unresolved imports are printed in details without inventing edges.
          Unreadable tsconfig plus unresolved bare imports makes the check Incomplete.

        Graph
          A file is a node. A directory is a module for cycle detection. Reexport-only
          barrels are transparent: named imports follow matching exports, while namespace
          imports reach all reexports. The report names the lines proving each cycle.

        Remediation
          Remove or reverse one import in the reported ring, or move the shared concept
          to a lower directory that both modules can depend on.

        Decision
          adrs/0064-typescript-language-axis.md
        """;

    private const string Ansible =
        """
        Rationale
          Roles that include each other cannot be applied, moved or reused in one direction:
          neither is complete without the other, and a change in one reaches both. That is a
          structural defect the harness can prove from tracked YAML, with the same core that
          proves module cycles in C#. ansible-playbook-grapher draws this graph; nothing judges it.

        Discovery
          Every tracked path under `roles/<name>/` names a role — the node. Tracked `.yml` and
          `.yaml` files outside toolchain-ignored `.` and `molecule/` directories are read
          lexically once the repository carries an
          Ansible marker. Playbooks compose roles and are not nodes.

        Evidence
          An edge is Proven when a role names another local role by a literal: an item of
          `dependencies:` in `meta/main.yml` (`- role: x` or `- x`), or the `name:` under
          `include_role` / `import_role` in `tasks/**` or `handlers/**`. A `name:` written in
          Jinja (`{{ role_to_apply }}`) is resolved at run time, graded Inferred, and cannot
          close a cycle. A role outside `roles/` — a collection or galaxy role — is external
          and never a node.

        Formula
          A role dependency cycle is a set of roles that all reach each other through Proven
          edges. The report names the shortest ring inside the group and the file and line of
          each edge that closes it; one finding per cycle.

        Why a cycle is blocking
          Every reported edge is a literal role name at a place Ansible reads it. The cycle
          has no valid application order, so required policy treats it as a violation.

        Limits
          The reader is lexical: an anchor, a merge key, `- { role: x }` in flow form and an
          include reached through `include_tasks` of another file read as absence. `roles:` in
          a playbook is not an edge, because the playbook is not a node.

        Remediation
          Read each edge in the reported ring and choose the intended direction. Usually the
          shared tasks belong in a third role that both may depend on, or one side declares
          the other in `meta/main.yml` `dependencies:` and drops its include.

        Applicability
          Disable every Ansible check together only when Ansible does not apply:

          "applicability": {
            "ansible": { "applicable": false, "reason": "why Ansible checks do not apply" }
          }

        Decisions
          adrs/0021-coupling-evidence-grades.md
          adrs/0057-ansible-axis.md
        """;

    public const string Text =
        """
        Rationale
          Modules that depend on each other cannot be ordered, moved or reused in one
          direction. That is a universal structural defect the harness can prove from
          tracked source. Raw dependency counts are different: a test, composition root or
          stable abstraction may correctly know or be known by many types. The current check
          therefore reports cycles and does not report fan-in or fan-out.

        Discovery
          Every Git-tracked `.cs` file is read. A generated suffix or header excludes a file
          only under a declared `generated` path. The harness does not build the repository
          or read compiler output.

        Resolution
          Each declared type is indexed by its simple name. A name is attributed to a
          declaration when exactly one candidate remains after considering its namespace,
          enclosing namespaces and imports. An ambiguous name is left out of the graph.

        Evidence
          A cycle uses only `Proven` references: the name stands where C# allows nothing but
          a type, such as a construction, base list, parameter, field, property, return type,
          generic argument, attribute, `typeof`, `sizeof`, `default` or `catch`. Member
          accesses, arguments and other name matches are `Inferred` and cannot close a cycle.

        Formula
          A module dependency cycle is a set of namespaces that all reach each other through
          proven references between their declared types. A namespace and its nested
          namespace are one module boundary here, not a dependency cycle.

        Why a cycle is blocking
          Every reported edge stands in a type-only position and resolves to one declaration.
          The cycle has no valid dependency order, so required policy treats it as a
          violation. The report names the shortest ring and the source lines that close it.

        What this check deliberately leaves elsewhere
          Repeated test setup belongs to `duplication.csharp`; allowed layer directions belong
          to the repository's semantic architecture tests. Type size, member groups and raw
          incoming or outgoing counts have no universal remediation and are not part of the
          current contract.

        Limits
          Nothing is bound by the compiler. Extension methods, overload resolution, implicit
          conversions, aliases, conditional compilation and reflection are not evaluated.
          A unique internal type with the same simple name as an external type can still be
          resolved incorrectly even in a type-only position. Partial types are merged.

        Policy
          A proved cycle is blocking when its explicit policy is required and cannot be
          suppressed by path. If the repository consciously accepts all findings from this check, tracked policy may
          make the whole check `advisory` or `off`; that broader decision stays visible in
          review.

        Remediation
          Read each edge in the reported ring and choose the intended direction. Usually the
          shared concept belongs in a third module that both sides may depend on, or one side
          should accept a smaller contract instead of naming the other module's concrete type.
        """;
}
