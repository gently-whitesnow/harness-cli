using Harness.Config;

namespace Harness.Checks.Frame;

/// <summary>Shared contract and question-specific context printed by `harness explain`.</summary>
internal static class FrameExplanations
{
    public static string UnitTests => For(
        "tests.unit",
        "tests that exercise units of this repository in isolation",
        "tests/Unit",
        "Only the repository knows which of its tests are unit tests; the harness accepts that classification.");

    public static string IntegrationTests => For(
        "tests.integration",
        "tests that exercise components together rather than in isolation",
        "tests/Integration",
        "The repository defines the boundary between integration and other test types.");

    public static string Architecture => For(
        "tests.architecture",
        "executable assertions about this repository's own structure",
        "tests/Architecture",
        "Architecture rules are product-specific; the repository reports whether it owns them.");

    public static string Format => For(
        "format",
        "a mechanically enforceable source format",
        ".editorconfig",
        "The harness neither runs a formatter nor checks that source obeys it; CI owns execution.");

    public static string Lint => For(
        "lint",
        "static analysis rules beyond formatting",
        ".globalconfig",
        "Rules may live in project files or build configuration, so an address is optional.");

    public static string Build => For(
        "build",
        "the entry point a reader should use to build this repository",
        "Repository.sln",
        "The harness records the repository's answer and leaves running the build to CI.");

    public static string Typecheck => For(
        "typecheck",
        "a type check that runs ahead of the code",
        "tsconfig.json",
        "A compiler-only repository can answer `applicable: false` with a reason.");

    public static string Verify => For(
        "verify",
        "one repository-owned entry point that runs every applicable quality check",
        "verify.sh",
        "This question applies to every repository, and only `paths` is a complete positive answer. The tracked "
            + "script composes the repository's toolchain and `harness check`; the harness records its address "
            + "but never executes it.");

    private static string For(string key, string subject, string address, string specific)
        => $$"""
        Rationale
          Every repository answers the same question — does it own {{subject}}? The answer is
          self-reported metadata for reviewers, agents and CI. The harness validates that the
          answer is complete and reports it; it does not search Git for support or contradiction.

        How to answer, in {{HarnessConfig.FileName}}
          "answers": {
            "{{key}}": { "paths": ["{{address}}"] }
          }

          "paths"              present, with navigation addresses; paths are not inspected
          "present" + reason   present or absent without a useful address
          "applicable" false   the question does not apply; a reason is required

        What each answer produces
          paths                       passed
          present true                passed
          present false               readiness gap
          applicable false            not applicable
          missing or malformed answer incomplete frame

        {{specific}}

        Remediation and policy
          A `required` policy makes an absent answer a violation. An explicit `advisory`
          policy accepts the gap without hiding it; `off`
          skips the question. Policy never makes the harness fact-check an answer.
        """;
}
