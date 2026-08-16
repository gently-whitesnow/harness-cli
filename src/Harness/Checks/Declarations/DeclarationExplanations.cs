using Harness.Checks.Surfaces;
using Harness.Config;

namespace Harness.Checks.Declarations;

/// <summary>
/// What `explain` prints for each declaration. The recognized-evidence lists are
/// interpolated from the same constants the checks search with, so the text cannot claim
/// the harness looked for something it did not.
/// </summary>
internal static class DeclarationExplanations
{
    public static string UnitTests => For(
        "tests.unit",
        "tests that exercise units of this repository in isolation",
        "tests/Unit",
        $"{RecognizedEvidence.List(DotNetSurface.TestProjectMarkers)} in a tracked project file, and a "
            + $"`package.json` script named one of {RecognizedEvidence.List(RecognizedEvidence.UnitTestScripts)}",
        """
        Why an address and not detection
          Git cannot tell a unit test from an integration test: both carry the same test SDK
          marker. Only the repository knows where its unit tests live, which is why the
          address is the fact here and why nothing found in Git is allowed to overrule it.
        """,
        refutable: false);

    public static string IntegrationTests => For(
        "tests.integration",
        "tests that exercise components together rather than in isolation",
        "tests/Integration",
        $"{RecognizedEvidence.List(RecognizedEvidence.IntegrationPackages)} in a tracked project file, a script "
            + $"named one of {RecognizedEvidence.List(RecognizedEvidence.IntegrationScripts)}, and a dependency on "
            + $"{RecognizedEvidence.List(RecognizedEvidence.IntegrationDependencies)}",
        """
        What counts
          A library whose purpose is running the real thing: an in-process host, a container,
          a browser driver. A test project on its own is not evidence of this.
        """);

    public static string Architecture => For(
        "tests.architecture",
        "executable assertions about this repository's own structure",
        "tests/Architecture",
        $"{RecognizedEvidence.List(RecognizedEvidence.ArchitecturePackages)} in a tracked project file, and a "
            + $"dependency on {RecognizedEvidence.List(RecognizedEvidence.ArchitectureDependencies)}",
        """
        What counts
          A library built to assert structure. Tests prove behaviour; only a structural
          assertion shows that structure is being held to anything.
        """);

    public static string Format => For(
        "format",
        "a pinned, mechanically enforceable source format",
        ".editorconfig",
        $"a tracked {RecognizedEvidence.List(RecognizedEvidence.FormatFiles)}, and a script named one of "
            + $"{RecognizedEvidence.List(RecognizedEvidence.FormatScripts)}",
        """
        What the harness does not do
          It does not run the formatter and does not judge whether the source obeys it. That
          is the repository's own pipeline; this asks only whether the rule exists and where.
        """);

    public static string Lint => For(
        "lint",
        "static analysis rules beyond formatting",
        ".globalconfig",
        $"a tracked {RecognizedEvidence.List(RecognizedEvidence.LintFiles)}, and a script named one of "
            + $"{RecognizedEvidence.List(RecognizedEvidence.LintScripts)}",
        """
        A note on .NET
          Analyzer configuration frequently lives in `Directory.Build.props` or a project
          file rather than a file of its own. That is a legitimate answer: give the address
          of whichever file carries it.
        """);

    public static string Build => For(
        "build",
        "a stated entry point for building this repository",
        "Repository.sln",
        "tracked solutions and project files, and a script named one of "
            + RecognizedEvidence.List(RecognizedEvidence.BuildScripts),
        """
        Why ask at all when Git shows the projects
          A repository with fifty projects still has one thing a newcomer or an agent is
          meant to build. Naming it is the answer; the project list is not.
        """);

    public static string Typecheck => For(
        "typecheck",
        "a type check that runs ahead of the code",
        "tsconfig.json",
        $"a tracked {RecognizedEvidence.List(RecognizedEvidence.TypecheckFiles)}, and a script named one of "
            + $"{RecognizedEvidence.List(RecognizedEvidence.TypecheckScripts)}",
        """
        When this does not apply
          A repository whose only language is checked by its compiler has no separate answer
          to give. Say so with `applicable: false` and a reason, rather than leaving it open.
        """);

    /// <summary>
    /// The shape every declaration explanation shares. Written once so that seven checks
    /// cannot end up describing seven different contracts.
    /// </summary>
    private static string For(
        string key,
        string subject,
        string address,
        string lookedFor,
        string specific,
        bool refutable = true)
        => $$"""
        Rationale
          Every repository is asked the same question — does it own {{subject}}, and where is
          the proof? — so that one frame reads the same way across all of them. The harness
          does not run the thing and does not infer that it exists: the repository answers
          for itself, in writing, and Git is what stops the answer from being free.

        How to answer, in {{HarnessConfig.FileName}}
          "declarations": {
            "{{key}}": { "paths": ["{{address}}"] }
          }

          "paths"              one or more tracked addresses. The strongest answer.
          "present" + reason   the thing exists but has no single address to point at.
          "applicable" false   the question does not apply here; a reason is required.

        What each answer produces
          address, all tracked        passed
          address, nothing tracked    violation — the frame claims something Git does not have
          present true                readiness gap — a claim the harness took and did not verify
          present false               readiness gap — a deliberate, explained absence
          applicable false            not applicable
          no answer at all            readiness gap — the question is open
        {{(refutable ? "  any answer Git refutes      violation" : "")}}
        What the harness looks for in Git
          {{lookedFor}}

        {{(refutable
            ? "  This list is only ever used to refute a denial. It never decides that the\n"
                + "  repository has the thing: recognizing nothing costs nothing, because the\n"
                + "  declaration already carries the claim."
            : "  This list only ever produces a hint about where the address might be. It never\n"
                + "  decides the answer and never contradicts one, because the same evidence\n"
                + "  appears in a repository that answers \"no\" truthfully.")}}

        {{specific.TrimEnd()}}

        Remediation
          Readiness gap: answer the question under "declarations.{{key}}" in
          {{HarnessConfig.FileName}}, preferring "paths" with a tracked address.
          Violation: point the declaration at what actually exists, or change the answer to
          match what Git shows. The harness never edits the frame on the repository's behalf.
          Raising the bar: set "policy": { "{{HarnessConfig.DeclarationGroup}}.{{key}}":
          "required" } to turn every readiness gap above into a violation, once this
          repository should no longer be allowed to leave the question open or answer "no".
        """;
}
