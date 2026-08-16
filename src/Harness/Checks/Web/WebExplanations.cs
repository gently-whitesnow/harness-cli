namespace Harness.Checks.Web;

/// <summary>Long-form content for `harness explain web.*`.</summary>
internal static class WebExplanations
{
    private const string Discovery =
        """
        Discovery
          The execution plan comes from Git-tracked evidence only: the package manifest, the
          lockfile that names the package manager that resolved it, and the scripts the
          manifest already declares. The root `package.json` is the plan; with no root
          manifest, a single tracked one is used, and several are an ambiguous plan.
          Generated, vendored and build-output locations are ignored. The package manager is
          never taken from the caller's environment or from a global preference, so the same
          repository is verified the same way by every developer. Conflicting evidence —
          two lockfiles, or a `packageManager` declaration that disagrees with the lockfile —
          ends the run as incomplete rather than choosing for the repository. A repository
          with no package manifest is reported as not applicable, never as a failure.
        """;

    private const string Boundaries =
        """
        Boundaries
          The harness runs only scripts the repository already declares; it never synthesizes
          a command, installs dependencies, edits a lockfile or applies a fix. A script that
          passes a mutating flag such as `--write` or `--fix` is reported rather than run. A
          missing script is a readiness gap: visible, never a pass and never a violation. A
          missing package manager, declared dependencies that are not installed, or a command
          that cannot be started ends the run as incomplete (exit code 2) and does not blame
          repository content. Normal build and test outputs under the conventional ignored
          directories are expected.
        """;

    private const string Evidence =
        """
        Evidence
          The script runs as `<package-manager> run <script>` from the manifest's directory,
          with language, colour and interactivity pinned so evidence does not depend on the
          caller's terminal or locale. A non-zero exit is a violation, located at the file
          and line the tool reported where its output can be read that way, and otherwise
          reported against the manifest with the tool's own diagnostic text.
        """;

    private const string Remediation =
        """
        Remediation
          Reproduce with the command shown in the report and fix what the tool reported. If
          the gate reported a readiness gap, add the missing verification script to the
          manifest; the harness will not add one for you.
        """;

    private static string Explain(string rationale)
        => $"""
        Rationale
          {rationale}

        {Discovery}

        {Evidence}

        {Boundaries}

        {Remediation}
        """;

    public static readonly string Format = Explain(
        """
        Formatting drift makes every later diff noisier than the change it carries. The
          repository's own formatting configuration decides what correct means; the gate only
          reports whether the tracked source already satisfies it, and only through a script
          that verifies rather than writes.
        """);

    public static readonly string Lint = Explain(
        """
        A lint rule the repository chose is a rule it wants enforced, and a rule enforced
          only by review is enforced unevenly. The gate runs the repository's own lint
          command and reports what it found; it has no opinion of its own about the rules.
        """);

    public static readonly string Typecheck = Explain(
        """
        A type error is a defect the repository's own compiler can prove, cheaply and
          without running anything. Proving it before review is the difference between a
          mechanical fix and a broken build someone else discovers.
        """);

    public static readonly string Test = Explain(
        """
        A repository's own tests are the only evidence of its intended behaviour that the
          harness can trust. It runs them; it does not judge what they should assert. The
          single-run script is preferred, because a watch-mode runner never finishes.
        """);

    public static readonly string Build = Explain(
        """
        Code that does not build cannot be shipped, and a build is the cheapest proof that
          the repository is in a coherent state — including the parts a type checker alone
          does not cover, such as bundling and asset resolution.
        """);
}
