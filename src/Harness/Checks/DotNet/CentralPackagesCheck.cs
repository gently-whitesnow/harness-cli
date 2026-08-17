using System.Xml.Linq;

namespace Harness.Checks.DotNet;

internal sealed class CentralPackagesCheck : DotNetCheck
{
    public override string Id => "central-packages.dotnet";

    public override string Group => "central-packages";

    public override string Summary => "central NuGet package versions";

    public override string Explanation => CentralPackagesExplanation.Text;

    protected override CheckEvaluation Inspect(CheckContext context, IReadOnlyList<DotNetFile> projects)
    {
        var findings = new List<Finding>();
        var referencing = false;

        foreach (var project in projects)
        {
            var references = DotNetRepository.Elements(project, "PackageReference").ToList();
            if (references.Count == 0)
            {
                continue;
            }

            referencing = true;
            var (packages, failure) = DotNetRepository.ReadNearest(
                context.Repository, project.Path, "Directory.Packages.props");
            if (failure is not null)
            {
                return CheckEvaluation.Incomplete(failure);
            }

            if (packages is null)
            {
                findings.Add(Block(
                    project.Path, "has PackageReference items but no tracked Directory.Packages.props"));
                continue;
            }

            Compare(project, packages, references, findings);
        }

        return referencing
            ? CheckEvaluation.From(findings)
            : CheckEvaluation.Passed("the .NET projects have no PackageReference items to centralize");
    }

    private static void Compare(
        DotNetFile project,
        DotNetFile packages,
        IReadOnlyList<XElement> references,
        List<Finding> findings)
    {
        if (!DotNetRepository.Elements(packages, "ManagePackageVersionsCentrally")
            .Any(element => Same(DotNetRepository.Value(element), "true")))
        {
            findings.Add(Block(
                packages.Path, $"must set ManagePackageVersionsCentrally to true for '{project.Path}'"));
        }

        var versions = CentralVersions(packages);
        foreach (var duplicate in versions.Where(entry => entry.Value.Count > 1))
        {
            findings.Add(Block(
                packages.Path,
                $"declares conflicting central versions for package '{duplicate.Key}' "
                    + $"({string.Join(", ", duplicate.Value)})"));
        }

        foreach (var reference in references)
        {
            Locate(project, packages, versions, reference, findings);
        }
    }

    private static void Locate(
        DotNetFile project,
        DotNetFile packages,
        Dictionary<string, List<string?>> versions,
        XElement reference,
        List<Finding> findings)
    {
        var name = Identity(reference);
        if (Version(reference) is not null || HasOverride(reference))
        {
            findings.Add(Block(
                project.Path,
                $"PackageReference '{name ?? "<unknown>"}' keeps a local version; move it to '{packages.Path}'"));
        }

        if (name is not null && (!versions.TryGetValue(name, out var declared) || declared.Count == 0))
        {
            findings.Add(Block(
                packages.Path, $"has no PackageVersion for '{name}' referenced by '{project.Path}'"));
        }
    }

    private static Dictionary<string, List<string?>> CentralVersions(DotNetFile packages)
        => DotNetRepository.Elements(packages, "PackageVersion")
            .Select(element => (Name: Identity(element), Version: Version(element)))
            .Where(item => item.Name is not null)
            .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.Version)
                    .Where(value => value is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

    private static bool HasOverride(XElement reference)
        => reference.Attribute("VersionOverride") is not null
            || reference.Elements().Any(element => element.Name.LocalName == "VersionOverride");

    private static string? Identity(XElement element)
        => element.Attribute("Include")?.Value.Trim() is { Length: > 0 } include
            ? include
            : element.Attribute("Update")?.Value.Trim() is { Length: > 0 } update ? update : null;

    private static string? Version(XElement element)
        => element.Attribute("Version")?.Value.Trim() is { Length: > 0 } attribute
            ? attribute
            : element.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "Version")?.Value.Trim() is { Length: > 0 } child
                ? child
                : null;

    private static bool Same(string? value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
