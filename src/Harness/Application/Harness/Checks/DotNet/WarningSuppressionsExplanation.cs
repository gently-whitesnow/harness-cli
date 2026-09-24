namespace Harness.Checks.DotNet;

internal static class WarningSuppressionsExplanation
{
    public const string Text =
        """
        Rationale
          build-properties.dotnet requires warnings to be errors; that baseline holds only while
          nobody silences the warnings one by one. Suppressions accumulate quietly and each looks
          reasonable in its own diff. The harness applies its own policy rule (ADR-0035) to the
          compiler's diagnostics: a rule is either on for the repository or off for it, and no
          file, project or path gets a private exception.

        What it reads
          Tracked .cs files except generated sources declared with paths and reason in
          .harness.json and bearing a toolchain marker, every tracked SDK-style project, the nearest
          Directory.Build.props above each project, and every tracked .editorconfig on the chain
          above a source, including shared files above a registered project directory. An
          .editorconfig section counts where its glob addresses a tracked source of this frame;
          a root section aimed only at a registered project is that project's suppression.
          The toolchain is not executed.

        What fails
          Address-level silencing: `#pragma warning disable` with any code, or with none;
          `[SuppressMessage]` and `[UnconditionalSuppressMessage]`; `NoWarn` and
          `WarningsNotAsErrors` inside a project file; `dotnet_diagnostic.<code>.severity` set to
          none, silent or suggestion in an .editorconfig section whose glob names a path or a
          name prefix (`[tests/**/*.cs]`, `[*.g.cs]`); and any
          `dotnet_analyzer_diagnostic` severity that silences a whole category.

        What is printed instead
          A rule switched off for the whole repository — `dotnet_diagnostic.<code>.severity =
          none` in a section such as `[*.cs]` or `[*]`, or `NoWarn` in Directory.Build.props — is
          the tracked, reviewable decision `policy: off` is for harness checks. It never fails
          the run only when settings.warning-suppressions.dotnet.repositoryWide names its id
          and reason. Every switch is printed in the ordinary report; undeclared and stale
          entries block the run.

        Remediation
          Fix the code the diagnostic points at. When the rule is wrong for this repository as a
          whole, switch it off for the whole repository and say why in the same file, as the
          reviewed settings.warning-suppressions.dotnet.repositoryWide entry does for CA1707. If the repository rejects this check entirely,
          record that through `policy.warning-suppressions.dotnet`.

        Applicability
          Disable all .NET repository checks together only when they do not apply:

          "applicability": {
            "dotnet": { "applicable": false, "reason": "why .NET checks do not apply" }
          }
        """;
}
