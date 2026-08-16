namespace Harness.Checks.DotNet;

/// <summary>Long-form content for `harness explain dotnet.*`.</summary>
internal static class DotNetExplanations
{
    private const string Discovery =
        """
        Discovery
          The execution plan comes from Git-tracked evidence only: solution files, project
          files and standard .NET tool manifests. No harness manifest is required, and
          generated, vendored and build-output locations are ignored. One tracked solution
          is the plan; with no solution, every tracked project is a target. More than one
          tracked solution is an ambiguous plan, and more than one plan is not a plan, so
          the gate ends the run as incomplete rather than choosing for the repository. A
          repository with no .NET evidence is reported as not applicable, never as a
          failure.
        """;

    private const string Boundaries =
        """
        Boundaries
          The harness never installs the SDK, never edits project files and never changes
          tracked source or configuration. A missing or unusable SDK, or a command that
          cannot be started, ends the run as incomplete (exit code 2) and does not blame
          repository content. So does a package restore failure: an unreachable or
          unauthenticated feed is an environment prerequisite, and NuGet's NU codes make it
          distinguishable from a defect in the code. A tracked NuGet lock file is honoured
          with locked-mode restore, so verification cannot rewrite it. Normal build and
          test outputs under the conventional ignored directories are expected.
        """;

    public static readonly string Format =
        $"""
        Rationale
          Formatting drift makes every later diff noisier than the change it carries. The
          repository's own formatting configuration decides what correct means; the gate
          only reports whether the tracked source already satisfies it.

        {Discovery}

        Evidence
          `dotnet format <target> --verify-no-changes` is a verification mode: it reports
          the files it would change and changes nothing. Each reported file and line
          becomes a located finding.

        {Boundaries}

        Remediation
          Run `dotnet format <target>` yourself to apply the fixes, then review and commit
          them. The harness will not format the repository for you.
        """;

    public static readonly string Build =
        $"""
        Rationale
          Code that does not compile cannot be reviewed, tested or measured. Compilation is
          the cheapest proof that the repository is in a coherent state, so it is blocking.

        {Discovery}

        Evidence
          `dotnet build <target>` runs through the installed SDK, so the result reflects the
          repository's real toolchain, target frameworks and analyzers rather than a
          reimplementation of them. Each compiler error becomes a finding located at the
          file and line the SDK reported.

        {Boundaries}

        Remediation
          Reproduce the failure with the command shown in the report and fix the reported
          diagnostics.
        """;

    public static readonly string Test =
        $"""
        Rationale
          A repository's own tests are the only evidence of its intended behaviour that the
          harness can trust. It runs them; it does not judge what they should assert.

        {Discovery}
          The gate applies only when a tracked project declares a .NET test framework —
          a `Microsoft.NET.Test.Sdk` reference or an explicit `IsTestProject` property. A
          repository without one is reported as not applicable, which is a visible absence
          of tests rather than a claim that the repository passed.

        Evidence
          `dotnet test <target>` builds and runs the discovered tests. Failing tests are
          reported by name; a test project that does not compile reports its compiler
          diagnostics instead. Gates are independent, so this gate builds what it runs and
          does not assume the compilation gate was selected.

        {Boundaries}

        Remediation
          Reproduce with the command shown in the report. The harness does not select a
          subset of affected tests, so a failure here is the repository's full test signal.
        """;
}
