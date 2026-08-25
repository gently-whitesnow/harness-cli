namespace Harness.Checks;

/// <summary>Long-form content for `harness explain docs.policy`.</summary>
internal static class DocumentationPolicyExplanation
{
    public const int LineLimit = 150;

    public static readonly string Text =
        $"""
        Rationale
          Agent navigation drifts when several root instruction documents exist and when
          those documents grow past what fits comfortably in an agent context. This check
          keeps one canonical root instruction document, keeps the agent entry points
          provably identical to it, and keeps durable rationale in one discoverable place.
          The names an agent opens by itself mean the same thing at every depth, so a
          nested document is judged by its name rather than by the directory holding it.

        Rules
          AGENTS.md    required at the root and allowed in any directory: a tracked regular
                       file of at most {LineLimit} physical lines, never a symbolic link.
          CLAUDE.md    required at the root and allowed beside any AGENTS.md: always a tracked
                       Git symbolic link whose direct relative target is the sibling AGENTS.md.
          README.md    optional overview at any depth, at most {LineLimit} physical lines.
          SKILL.md     allowed at any depth and not measured: an agent skill is a payload
                       loaded on demand for one task, not navigation carried in every context.
          adrs/**.md   allowed as durable architectural decisions.
          other *.md   blocking violation by default.

        Evidence
          Only Git-tracked Markdown is considered, so generated, vendored and build-output
          content cannot create noise. Symbolic links are read from the Git index, so a
          regular-file copy, a chained link, a broken link, an absolute link and a link to
          another target are all distinguishable from a correct direct relative link.
          Non-Markdown contracts such as OpenAPI documents and schemas are out of scope.
          Line counts are measured on the working tree, so an edit is judged before it is
          staged; when a tracked document is absent there, its staged content is used
          instead. Evidence that cannot be read at all ends the check as incomplete.

        Remediation
          Blocking findings: create AGENTS.md if it is missing, shorten a document that
          exceeds the line limit, and replace CLAUDE.md with a direct relative symbolic
          link, for example `ln -sf AGENTS.md CLAUDE.md && git add CLAUDE.md`.
          Unexpected Markdown: remove the document, rename it to the name an agent already
          opens in that directory, fold its navigation into AGENTS.md, move durable rationale
          into an ADR under adrs/, set `policy.docs.policy` to `advisory` or `off`, or add a
          named `suppress` exception with a reason. The harness never edits documentation.
        """;
}
