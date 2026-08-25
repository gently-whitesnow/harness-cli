using System.Xml.Linq;

namespace Harness.Checks.DotNet;

internal sealed class BuildPropertiesCheck : DotNetCheck
{
    private const string ContinuousIntegration = "ContinuousIntegrationBuild";

    private static readonly EvidenceFile BuildProps = new("Directory.Build.props");

    private static readonly IReadOnlyDictionary<string, string> Required =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Nullable"] = "enable",
            ["ImplicitUsings"] = "enable",
            ["TreatWarningsAsErrors"] = "true",
            ["EnableNETAnalyzers"] = "true",
            ["AnalysisLevel"] = "latest-Recommended",
            ["EnforceCodeStyleInBuild"] = "true",
            ["Deterministic"] = "true",
        };

    public override string Id => "build-properties.dotnet";

    public override string Group => "build-properties";

    public override string Summary => "central hardened .NET build properties";

    public override string Explanation => BuildPropertiesExplanation.Text;

    protected override IReadOnlyList<EvidenceFile> PolicyFiles => [BuildProps];

    protected override CheckEvaluation Inspect(CheckContext context, IReadOnlyList<DotNetFile> projects)
    {
        var findings = new List<Finding>();
        foreach (var project in projects)
        {
            var (props, failure) = DotNetRepository.ReadNearest(context, project.Path, BuildProps);
            if (failure is not null)
            {
                return CheckEvaluation.Incomplete(failure);
            }

            if (props is null)
            {
                findings.Add(Block(project.Path, "is not covered by a tracked Directory.Build.props"));
                continue;
            }

            RequireProperties(props, project, findings);
            RequireContinuousIntegration(props, project, findings);
            RejectLocalOverrides(project, findings);
        }

        AddSharedTargetFrameworkFinding(projects, findings);
        return CheckEvaluation.From(findings);
    }

    private static void RequireProperties(DotNetFile props, DotNetFile project, List<Finding> findings)
    {
        foreach (var expected in Required)
        {
            var values = DotNetRepository.Elements(props, expected.Key)
                .Select(DotNetRepository.Value)
                .Where(value => value is not null)
                .ToList();

            if (!values.Any(value => Same(value, expected.Value)))
            {
                findings.Add(Block(
                    props.Path, $"must set {expected.Key} to {expected.Value} for '{project.Path}'"));
            }

            foreach (var value in values.Where(value => !Same(value, expected.Value)))
            {
                findings.Add(Block(
                    props.Path,
                    $"sets {expected.Key} to conflicting value '{value}' for '{project.Path}'"));
            }
        }
    }

    private static void RequireContinuousIntegration(DotNetFile props, DotNetFile project, List<Finding> findings)
    {
        var declared = DotNetRepository.Elements(props, ContinuousIntegration).ToList();
        if (!declared.Any(element => Same(DotNetRepository.Value(element), "true") && HasCondition(element)))
        {
            findings.Add(Block(
                props.Path,
                $"must set {ContinuousIntegration} to true under a CI condition for '{project.Path}'"));
        }

        foreach (var value in declared
            .Select(DotNetRepository.Value)
            .Where(value => value is not null && !Same(value, "true")))
        {
            findings.Add(Block(
                props.Path,
                $"sets {ContinuousIntegration} to conflicting value '{value}' for '{project.Path}'"));
        }
    }

    private static void RejectLocalOverrides(DotNetFile project, List<Finding> findings)
    {
        var central = Required.Append(new KeyValuePair<string, string>(ContinuousIntegration, "true"));
        foreach (var expected in central)
        {
            foreach (var declaration in DotNetRepository.Elements(project, expected.Key))
            {
                var value = DotNetRepository.Value(declaration);
                if (value is not null && !Same(value, expected.Value))
                {
                    findings.Add(Block(
                        project.Path,
                        $"overrides central {expected.Key} with '{value}', weakening the repository baseline"));
                }
            }
        }
    }

    private static void AddSharedTargetFrameworkFinding(IReadOnlyList<DotNetFile> projects, List<Finding> findings)
    {
        if (projects.Count < 2)
        {
            return;
        }

        var declarations = projects
            .Select(project => (Project: project, Values: TargetFrameworksOf(project)))
            .ToList();

        if (declarations.All(entry => entry.Values.Count == 1)
            && declarations.Select(entry => entry.Values[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
        {
            foreach (var entry in declarations)
            {
                findings.Add(Block(
                    entry.Project.Path,
                    $"repeats shared TargetFramework '{entry.Values[0]}'; move it to Directory.Build.props"));
            }
        }
    }

    private static List<string?> TargetFrameworksOf(DotNetFile project)
        => DotNetRepository.Elements(project, "TargetFramework")
            .Concat(DotNetRepository.Elements(project, "TargetFrameworks"))
            .Select(DotNetRepository.Value)
            .Where(value => value is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool HasCondition(XElement element)
        => !string.IsNullOrWhiteSpace(element.Attribute("Condition")?.Value)
            || !string.IsNullOrWhiteSpace(element.Parent?.Attribute("Condition")?.Value);

    private static bool Same(string? value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
