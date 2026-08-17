namespace Harness.Checks.DotNet;

internal sealed class CentralPackagesCheck : IRepositoryCheck
{
    public string Id => "central-packages.dotnet";
    public string Group => "central-packages";
    public string Applicability => "dotnet";
    public string Summary => "central NuGet package versions";
    public string Explanation => CentralPackagesExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (projects, failure) = DotNetRepository.ReadProjects(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (projects.Count == 0)
        {
            return CheckEvaluation.NotApplicable("no tracked SDK-style .NET projects were found");
        }

        var findings = new List<Finding>();
        var hasReferences = false;
        foreach (var project in projects)
        {
            var references = DotNetRepository.Elements(project, "PackageReference").ToList();
            if (references.Count == 0)
            {
                continue;
            }

            hasReferences = true;
            var (packages, packagesFailure) = DotNetRepository.ReadNearest(context.Repository, project.Path, "Directory.Packages.props");
            if (packagesFailure is not null)
            {
                return CheckEvaluation.Incomplete(packagesFailure);
            }

            if (packages is null)
            {
                findings.Add(Block(project.Path, "has PackageReference items but no tracked Directory.Packages.props"));
                continue;
            }

            var central = DotNetRepository.Elements(packages, "ManagePackageVersionsCentrally")
                .Any(element => string.Equals(DotNetRepository.Value(element), "true", StringComparison.OrdinalIgnoreCase));
            if (!central)
            {
                findings.Add(Block(packages.Path, $"must set ManagePackageVersionsCentrally to true for '{project.Path}'"));
            }

            var versions = DotNetRepository.Elements(packages, "PackageVersion")
                .Select(element => (Name: Identity(element), Version: Version(element)))
                .Where(item => item.Name is not null)
                .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Version).Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var duplicate in versions.Where(entry => entry.Value.Count > 1))
            {
                findings.Add(Block(packages.Path,
                    $"declares conflicting central versions for package '{duplicate.Key}' ({string.Join(", ", duplicate.Value)})"));
            }

            foreach (var reference in references)
            {
                var name = Identity(reference) ?? "<unknown>";
                if (Version(reference) is not null || reference.Attribute("VersionOverride") is not null
                    || reference.Elements().Any(element => element.Name.LocalName == "VersionOverride"))
                {
                    findings.Add(Block(project.Path, $"PackageReference '{name}' keeps a local version; move it to '{packages.Path}'"));
                }

                if (name != "<unknown>" && (!versions.TryGetValue(name, out var declared) || declared.Count == 0))
                {
                    findings.Add(Block(packages.Path, $"has no PackageVersion for '{name}' referenced by '{project.Path}'"));
                }
            }
        }

        return hasReferences
            ? CheckEvaluation.From(findings)
            : CheckEvaluation.Passed("the .NET projects have no PackageReference items to centralize");
    }

    private static string? Identity(System.Xml.Linq.XElement element)
        => element.Attribute("Include")?.Value.Trim() is { Length: > 0 } include
            ? include
            : element.Attribute("Update")?.Value.Trim() is { Length: > 0 } update ? update : null;

    private static string? Version(System.Xml.Linq.XElement element)
        => element.Attribute("Version")?.Value.Trim() is { Length: > 0 } attribute
            ? attribute
            : element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value.Trim() is { Length: > 0 } child
                ? child
                : null;

    private static Finding Block(string location, string message)
        => new(FindingSeverity.Blocking, location, message);
}
