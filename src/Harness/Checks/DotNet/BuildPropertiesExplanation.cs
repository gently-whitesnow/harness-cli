namespace Harness.Checks.DotNet;

internal static class BuildPropertiesExplanation
{
    public const string Text =
        """
        Rationale
          Shared build policy belongs in Directory.Build.props so new and existing SDK-style
          projects receive the same nullable, analyzer, warning, style and reproducibility baseline.

        What it reads
          Tracked SDK-style .csproj, .fsproj and .vbproj files and the nearest tracked
          Directory.Build.props above each project. The repository toolchain is not executed.
          Tracked means present in the Git index, so a props file written without `git add` counts
          as missing; the run then names it under `not in the index`, as it does for a project.

        What fails
          A project without Directory.Build.props; a missing hardened property; an unconditional
          ContinuousIntegrationBuild; a project override that weakens the baseline; or the same
          TargetFramework repeated by every project instead of centralized once.

        Remediation
          Put shared values in the nearest Directory.Build.props. Keep genuinely different target
          frameworks in project files. A deliberate exception uses `suppress` with this check,
          the affected path and a non-empty reason.

        Applicability
          Disable all .NET repository checks together only when they do not apply:

          "applicability": {
            "dotnet": { "applicable": false, "reason": "why .NET checks do not apply" }
          }
        """;
}
