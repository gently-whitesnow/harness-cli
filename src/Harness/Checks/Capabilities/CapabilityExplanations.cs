using Harness.Checks.DotNet;
using Harness.Checks.Web;

namespace Harness.Checks.Capabilities;

/// <summary>
/// Long-form content for `harness explain capability.*`. The evidence lists are interpolated
/// from the ones the checks actually use, so this text cannot come to describe a version of
/// the harness that no longer exists.
/// </summary>
internal static class CapabilityExplanations
{
    private const string Vocabulary =
        """
        Vocabulary
          detected       recognized evidence of the capability is tracked in the repository.
          executed       a gate ran that same evidence in this run and it passed. A gate of
                         the same stack that ran something else does not count.
          not detected   the harness looked for the evidence listed below and found none of
                         it. This is a statement about what was looked for, never a claim
                         that the repository has no such capability.
          unknown        there is evidence and it does not settle the question, for example
                         a repository whose execution plan is ambiguous or a project file
                         that could not be read.
          not applicable there is no stack the capability could live in.
        """;

    private const string Boundaries =
        """
        Boundaries
          Capability evidence is advisory in v0. A missing or uncertain capability is a
          readiness gap: visible in the report, never a pass on its own, never a violation,
          and never an exit code 1. The list of recognized evidence lives in the harness, so
          it is always potentially behind the repository being read; that is exactly why a
          negative result is phrased as "not detected" and never as "absent".

          The harness reads tracked evidence and the outcome of gates that already ran. It
          does not run an extra command to answer a capability question, does not generate
          the missing capability, and does not produce a single readiness score: a number
          would invite comparison between repositories that were never measured the same
          way, and would hide which of these five statements it was built from.
        """;

    public static readonly string Tests =
        $"""
        Rationale
          Whether a repository owns automated tests at all is the first thing a reader —
          human or agent — needs before trusting anything else about it. This check answers
          only that, from the evidence the test gates already plan from.

        {Vocabulary}

        Evidence
          .NET: a tracked project that declares a test framework, through one of
          {RecognizedEvidence.List(DotNetSurface.TestProjectMarkers)}. These are the markers
          the SDK itself uses, so detection does not depend on a naming convention, and the
          classification is discovery's rather than a second opinion about it.
          Web: one of the scripts {RecognizedEvidence.List(WebScriptNames.Test)} in the
          tracked package manifest.
          The evidence is raised to `executed` when the gate that runs it — `dotnet.test` or
          `web.test` — ran in the same run and passed. Skipping those gates lowers the
          evidence to `detected`, which is what actually happened.

        False positives and limits
          A project can declare a test framework and contain no meaningful assertions, and a
          `test` script can be a placeholder. This check reports that tests exist and, at
          most, that they pass. It never reports what they cover.
          `executed` reads solution membership no further than the SDK reports it: when the
          plan is a solution, a tracked test project the solution does not include would not
          have run, and the harness does not parse solution membership to find out. Treat
          `executed` as "the test command passed and this project is a test project".

        {Boundaries}

        Remediation
          Add tests where the report shows none, and prefer running them through the gate so
          the evidence is `executed` rather than merely present.
        """;

    public static readonly string Integration =
        $"""
        Rationale
          Unit tests and tests that exercise components together fail for different reasons
          and protect against different mistakes. A repository that has only the first has a
          gap worth seeing, and one whose integration tests exist is worth recording as
          evidence rather than assumed from a directory name.

        {Vocabulary}

        Evidence
          .NET: a tracked project that references a library whose purpose is running the
          real thing — {RecognizedEvidence.List(RecognizedEvidence.IntegrationPackages)}.
          Web: one of the scripts
          {RecognizedEvidence.List(RecognizedEvidence.IntegrationScripts)}, or a declared
          dependency on {RecognizedEvidence.List(RecognizedEvidence.IntegrationDependencies)}.
          On .NET this can reach `executed`, because `dotnet test` runs the test project the
          evidence sits in. On the web side it cannot: `web.test` runs the repository's test
          script, which is not the end-to-end runner, so a passing `web.test` is never taken
          as evidence that the end-to-end suite ran.

        False positives and limits
          A project name is deliberately not evidence here. A project called
          `IntegrationTests` may contain nothing but unit tests, and a project that starts
          the application in process is an integration test whatever it is called. A
          referenced package is likewise no proof that the tests using it are meaningful:
          it is proof that the repository set out to write them.

        {Boundaries}

        Remediation
          Where the capability is not detected, add integration tests through the repository's
          own toolchain. Where it is detected, remember the report says they exist, not that
          they cover the paths that matter.
        """;

    public static readonly string Architecture =
        $"""
        Rationale
          Repository-specific architectural rules belong in the repository, as executable
          tests it owns. The harness does not infer what architecture a repository intended
          and does not enforce one; it reports whether the repository asserts its own rules
          where a reader can find them.

        {Vocabulary}

        Evidence
          .NET: a tracked project that references an architecture-rule library —
          {RecognizedEvidence.List(RecognizedEvidence.ArchitecturePackages)}.
          Web: a declared dependency on
          {RecognizedEvidence.List(RecognizedEvidence.ArchitectureDependencies)}. No shipped
          gate runs a boundary linter, so web evidence never reaches `executed`.
          A test project on its own is never evidence here. Tests prove behaviour; only a
          library built to assert structure shows that structure is being asserted.

        False positives and limits
          Detection is per project, and the report says how many other tracked projects the
          evidence says nothing about. That count is not a coverage measurement: one rule
          suite can legitimately govern an entire repository, and a large one can govern
          almost nothing. Treat a growing count as a question to ask, not a defect to fix.
          A repository may also enforce boundaries through compilation, package layout or
          review, none of which this check can see.

        {Boundaries}

        Remediation
          Where architecture matters and nothing is detected, add rules as tests in the
          repository rather than as prose an agent must be told to follow. Do not add a
          recognized package merely to change this line.
        """;
}
