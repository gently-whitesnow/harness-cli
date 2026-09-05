using System.Xml;
using System.Xml.Linq;
using Harness.Repository;

namespace Harness.Checks.DotNet;

internal sealed class SolutionFormatCheck : DotNetCheck
{
    private static readonly EvidenceFile Solution = new("*.slnx");

    private static readonly EvidenceFile LegacySolution = new("*.sln");

    public override string Id => "solution-format.dotnet";

    public override string Group => "solution-format";

    public override string Summary => "XML solution format and project coverage";

    public override string Explanation => SolutionFormatExplanation.Text;

    protected override IReadOnlyList<EvidenceFile> PolicyFiles => [Solution, LegacySolution];

    protected override CheckEvaluation Inspect(CheckContext context, IReadOnlyList<DotNetFile> projects)
    {
        var findings = context.Tracked(LegacySolution)
            .Select(entry => Block(
                entry.Path, "legacy .sln is tracked; migrate it to .slnx and remove the .sln file"))
            .ToList();

        var solutions = context.Tracked(Solution);

        if (solutions.Count == 0)
        {
            if (projects.Count > 1)
            {
                findings.Add(Block(
                    projects[0].Path,
                    $"repository has {projects.Count} SDK-style projects but no tracked .slnx solution"));
            }

            return CheckEvaluation.From(findings);
        }

        var (covered, failure) = Covered(context, solutions);
        if (covered is null)
        {
            return CheckEvaluation.Incomplete(failure!);
        }

        findings.AddRange(projects
            .Where(project => !covered.Contains(project.Path))
            .Select(project => Block(project.Path, "is not included in any tracked .slnx solution")));

        return CheckEvaluation.From(findings);
    }

    private static (HashSet<string>? Covered, string? Failure) Covered(
        CheckContext context,
        IReadOnlyList<TrackedEntry> solutions)
    {
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in solutions)
        {
            var (solution, failure) = ReadSolution(context, entry);
            if (solution is null)
            {
                return (null, failure);
            }

            foreach (var element in DotNetRepository.Elements(solution, "Project"))
            {
                var path = element.Attribute("Path")?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    covered.Add(DotNetRepository.NormalizeRelative(entry.Path, path));
                }
            }
        }

        return (covered, null);
    }

    private static (DotNetFile? File, string? Failure) ReadSolution(CheckContext context, TrackedEntry entry)
    {
        var (text, readFailure) = context.Repository.ReadTrackedText(entry);
        if (text is null)
        {
            return (null, readFailure);
        }

        try
        {
            var root = XDocument.Parse(text).Root;
            return root is null
                ? (null, $"'{entry.Path}' has no XML root element")
                : (new DotNetFile(entry.Path, root), null);
        }
        catch (XmlException exception)
        {
            return (null, $"'{entry.Path}' is not readable XML ({exception.Message})");
        }
    }
}
