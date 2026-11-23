namespace Harness.Checks.TypeScript;

/// <summary>Long-form content for `harness explain lint-suppressions.typescript`.</summary>
internal static class TypeScriptLintSuppressionsExplanation
{
    public const string Text = """
        Rationale
          A typechecker or linter only protects a codebase while nobody silences it one
          finding at a time. Address-level suppressions let an agent bypass type errors or
          lint rules without fixing the underlying code.

        Rules
          Comments in tracked TypeScript and JavaScript sources must not contain address-level
          suppression directives: `@ts-ignore`, `@ts-nocheck`, `@ts-expect-error`,
          `eslint-disable` (`eslint-disable-next-line`, `eslint-disable-line`),
          `biome-ignore` or `oxlint-disable` (`oxlint-disable-next-line`,
          `oxlint-disable-line`). String literals are skipped.

        Evidence
          Tracked `.ts`, `.tsx`, `.mts`, `.cts`, `.js`, `.jsx`, `.mjs` and `.cjs` files read
          from the Git index and working tree. No external linter or compiler is executed.

        Remediation
          Fix the underlying type or lint issue and remove the suppression comment.
        """;
}
