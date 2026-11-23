namespace Harness.Checks.TypeScript;

/// <summary>Long-form content for `harness explain tsconfig.typescript`.</summary>
internal static class TypeScriptConfigExplanation
{
    public const string Text = """
        Rationale
          TypeScript only protects a codebase while strict typechecking is enabled and covers
          every source file. Without a tracked tsconfig.json or with `strict` disabled, type
          errors turn into silent `any` holes. This check reads tracked tsconfig files from
          the Git index without invoking `tsc`.

        Rules
          1. Every tracked `.ts`, `.tsx`, `.mts` or `.cts` source (excluding `.d.ts` ambient
             declarations) must have a tracked `tsconfig.json` in its directory or an ancestor
             directory.
          2. Every tracked `tsconfig.json` and `tsconfig.*.json` that compiles sources (not a
             solution-style references-only root with `"files": []` and `"references"`) must
             enable `"strict": true` (directly or through a local `"extends"` chain) and must
             not set `"strict"`, `"noImplicitAny"` or `"strictNullChecks"` to `false`.

        Evidence
          Tracked TypeScript sources and `tsconfig*.json` files read from the Git index and
          working tree; comments and trailing commas (JSONC) are supported. Repositories with
          only JavaScript files and no TypeScript sources are reported as not applicable.

        Remediation
          Add a tracked `tsconfig.json` covering the TypeScript sources and set
          `"strict": true` inside `compilerOptions`, removing any `"noImplicitAny": false` or
          `"strictNullChecks": false` overrides.
        """;
}
