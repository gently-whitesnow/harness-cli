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

        Rules
          ROOT.md      required, tracked, at most {LineLimit} physical lines.
          AGENTS.md    required tracked Git symbolic link whose direct relative target is ROOT.md.
          CLAUDE.md    required tracked Git symbolic link whose direct relative target is ROOT.md.
          README.md    optional root overview, at most {LineLimit} physical lines.
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
          Blocking findings: create ROOT.md if it is missing, shorten a document that
          exceeds the line limit, and replace AGENTS.md or CLAUDE.md with a direct relative
          symbolic link, for example `ln -sf ROOT.md AGENTS.md && git add AGENTS.md`.
          Unexpected Markdown: remove the document, fold its navigation into ROOT.md, move
          durable rationale into an ADR under adrs/, set `policy.docs.policy` to `advisory`
          or `off`, or add a named `suppress` exception with a reason. The harness never
          edits documentation.
        """;
}
