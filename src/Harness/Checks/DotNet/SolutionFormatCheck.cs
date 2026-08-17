namespace Harness.Checks.DotNet;

internal sealed class SolutionFormatCheck : IRepositoryCheck
{
    public string Id => "solution-format.dotnet";
    public string Group => "solution-format";
    public string Applicability => "dotnet";
    public string Summary => "XML solution format and project coverage";
    public string Explanation => SolutionFormatExplanation.Text;

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

        var findings = context.Repository.TrackedEntries
            .Where(entry => entry.Path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new Finding(FindingSeverity.Blocking, entry.Path,
                "legacy .sln is tracked; migrate it to .slnx and remove the .sln file"))
            .ToList();

        var solutions = context.Repository.TrackedEntries
            .Where(entry => entry.Path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (projects.Count > 1 && solutions.Count == 0)
        {
            findings.Add(new Finding(FindingSeverity.Blocking, projects[0].Path,
                $"repository has {projects.Count} SDK-style projects but no tracked .slnx solution"));
            return CheckEvaluation.From(findings);
        }

        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in solutions)
        {
            var (solution, solutionFailure) = ReadSolution(context, entry);
            if (solutionFailure is not null)
            {
                return CheckEvaluation.Incomplete(solutionFailure);
            }

            foreach (var element in DotNetRepository.Elements(solution!, "Project"))
            {
                var path = element.Attribute("Path")?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    covered.Add(DotNetRepository.NormalizeRelative(entry.Path, path));
                }
            }
        }

        if (solutions.Count > 0)
        {
            foreach (var project in projects.Where(project => !covered.Contains(project.Path)))
            {
                findings.Add(new Finding(FindingSeverity.Blocking, project.Path,
                    "is not included in any tracked .slnx solution"));
            }
        }

        return CheckEvaluation.From(findings);
    }

    private static (DotNetFile? File, string? Failure) ReadSolution(CheckContext context, Harness.Git.TrackedEntry entry)
    {
        var (text, readFailure) = context.Repository.ReadTrackedText(entry);
        if (text is null)
        {
            return (null, readFailure);
        }

        try
        {
            var root = System.Xml.Linq.XDocument.Parse(text).Root;
            return root is null
                ? (null, $"'{entry.Path}' has no XML root element")
                : (new DotNetFile(entry.Path, root), null);
        }
        catch (System.Xml.XmlException exception)
        {
            return (null, $"'{entry.Path}' is not readable XML ({exception.Message})");
        }
    }
}
