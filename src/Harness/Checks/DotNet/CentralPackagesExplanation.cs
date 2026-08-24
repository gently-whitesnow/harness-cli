namespace Harness.Checks.DotNet;

internal static class CentralPackagesExplanation
{
    public const string Text =
        """
        Rationale
          Central Package Management gives every project in a scope one reviewed version for
          each NuGet dependency and removes version drift from individual project files.

        What it reads
          PackageReference items in tracked SDK-style projects and the nearest tracked
          Directory.Packages.props above each project. NuGet restore is not executed.

        What fails
          A referenced package without a Directory.Packages.props scope, central management not
          enabled, a local Version or VersionOverride, a missing PackageVersion, or conflicting
          central versions in the same file.

        Remediation
          Set ManagePackageVersionsCentrally in the nearest Directory.Packages.props, declare
          PackageVersion items there, and remove versions from PackageReference items. Scoped
          files such as apps/api/Directory.Packages.props are supported.
        """;
}
