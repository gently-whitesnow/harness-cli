namespace Harness.Checks.DotNet;

internal sealed class BuildPropertiesCheck : IRepositoryCheck
{
    private static readonly IReadOnlyDictionary<string, string> Required = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Nullable"] = "enable",
        ["ImplicitUsings"] = "enable",
        ["TreatWarningsAsErrors"] = "true",
        ["EnableNETAnalyzers"] = "true",
        ["AnalysisLevel"] = "latest-Recommended",
        ["EnforceCodeStyleInBuild"] = "true",
        ["Deterministic"] = "true",
    };

    public string Id => "build-properties.dotnet";
    public string Group => "build-properties";
    public string Applicability => "dotnet";
    public string Summary => "central hardened .NET build properties";
    public string Explanation => BuildPropertiesExplanation.Text;

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
        foreach (var project in projects)
        {
            var (props, propsFailure) = DotNetRepository.ReadNearest(context.Repository, project.Path, "Directory.Build.props");
            if (propsFailure is not null)
            {
                return CheckEvaluation.Incomplete(propsFailure);
            }

            if (props is null)
            {
                findings.Add(Block(project.Path, "is not covered by a tracked Directory.Build.props"));
                continue;
            }

            foreach (var expected in Required)
            {
                var values = DotNetRepository.Elements(props, expected.Key).Select(DotNetRepository.Value).Where(value => value is not null).ToList();
                if (!values.Any(value => string.Equals(value, expected.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(Block(props.Path, $"must set {expected.Key} to {expected.Value} for '{project.Path}'"));
                }

                foreach (var value in values.Where(value => !string.Equals(
                    value,
                    expected.Value,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(Block(props.Path,
                        $"sets {expected.Key} to conflicting value '{value}' for '{project.Path}'"));
                }
            }

            var continuous = DotNetRepository.Elements(props, "ContinuousIntegrationBuild")
                .Any(element => string.Equals(DotNetRepository.Value(element), "true", StringComparison.OrdinalIgnoreCase)
                    && HasCondition(element));
            if (!continuous)
            {
                findings.Add(Block(props.Path,
                    $"must set ContinuousIntegrationBuild to true under a CI condition for '{project.Path}'"));
            }


            foreach (var value in DotNetRepository.Elements(props, "ContinuousIntegrationBuild")
                .Select(DotNetRepository.Value)
                .Where(value => value is not null && !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(Block(props.Path,
                    $"sets ContinuousIntegrationBuild to conflicting value '{value}' for '{project.Path}'"));
            }

            foreach (var expected in Required.Append(new KeyValuePair<string, string>("ContinuousIntegrationBuild", "true")))
            {
                foreach (var declaration in DotNetRepository.Elements(project, expected.Key))
                {
                    var value = DotNetRepository.Value(declaration);
                    if (value is not null && !string.Equals(value, expected.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(Block(project.Path,
                            $"overrides central {expected.Key} with '{value}', weakening the repository baseline"));
                    }
                }
            }
        }

        AddSharedTargetFrameworkFinding(projects, findings);
        return CheckEvaluation.From(findings);
    }

    private static void AddSharedTargetFrameworkFinding(IReadOnlyList<DotNetFile> projects, List<Finding> findings)
    {
        if (projects.Count < 2)
        {
            return;
        }

        var declarations = projects
            .Select(project => (Project: project, Values: DotNetRepository.Elements(project, "TargetFramework")
                .Concat(DotNetRepository.Elements(project, "TargetFrameworks"))
                .Select(DotNetRepository.Value)
                .Where(value => value is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()))
            .ToList();

        if (declarations.All(entry => entry.Values.Count == 1)
            && declarations.Select(entry => entry.Values[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
        {
            foreach (var entry in declarations)
            {
                findings.Add(Block(entry.Project.Path,
                    $"repeats shared TargetFramework '{entry.Values[0]}'; move it to Directory.Build.props"));
            }
        }
    }

    private static bool HasCondition(System.Xml.Linq.XElement element)
        => !string.IsNullOrWhiteSpace(element.Attribute("Condition")?.Value)
            || !string.IsNullOrWhiteSpace(element.Parent?.Attribute("Condition")?.Value);

    private static Finding Block(string location, string message)
        => new(FindingSeverity.Blocking, location, message);
}
