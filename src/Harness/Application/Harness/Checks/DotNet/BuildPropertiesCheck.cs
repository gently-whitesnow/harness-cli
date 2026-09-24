using System.Xml.Linq;
using Harness.Repository;

namespace Harness.Checks.DotNet;

internal sealed class BuildPropertiesCheck : DotNetCheck
{
    private const string ContinuousIntegration = "ContinuousIntegrationBuild";

    private static readonly EvidenceFile BuildProps = new("Directory.Build.props", Inherited: true);
    private static readonly EvidenceFile BuildTargets = new("Directory.Build.targets", Inherited: true);

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

    protected override IReadOnlyList<EvidenceFile> PolicyFiles => [BuildProps, BuildTargets, new EvidenceFile("*.props"), new EvidenceFile("*.targets")];

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

            var imports = ReadLocalImports(context, props, findings);
            var effective = new DotNetFile(props.Path,
                new XElement("Project", props.Root.Elements(), imports.SelectMany(imported => imported.Root.Elements())));
            RequireProperties(effective, project, findings);
            RequireContinuousIntegration(effective, project, findings);
            RejectLocalOverrides(project, findings);
            RejectWeakening(props, findings);
            foreach (var imported in imports)
            {
                RejectWeakening(imported, findings);
            }
            RejectWeakening(project, findings);
            RejectUnresolvedImports(props, findings);
            RejectUnresolvedImports(project, findings);
            foreach (var imported in ReadLocalImports(context, project, findings))
            {
                RejectWeakening(imported, findings);
                RejectUnresolvedImports(imported, findings);
            }
            var (targets, targetFailure) = DotNetRepository.ReadNearest(context, project.Path, BuildTargets);
            if (targetFailure is not null)
            {
                return CheckEvaluation.Incomplete(targetFailure);
            }

            if (targets is not null)
            {
                RejectWeakening(targets, findings);
                RejectUnresolvedImports(targets, findings);
                RejectLocalOverrides(targets, findings);
                foreach (var imported in ReadLocalImports(context, targets, findings))
                {
                    RejectWeakening(imported, findings);
                    RejectUnresolvedImports(imported, findings);
                }
            }
        }

        AddSharedTargetFrameworkFinding(projects, findings);
        return CheckEvaluation.From(findings);
    }

    private static void RequireProperties(DotNetFile props, DotNetFile project, List<Finding> findings)
    {
        foreach (var expected in Required)
        {
            var values = DotNetRepository.Elements(props, expected.Key)
                .Select(element => (Value: DotNetRepository.Value(element), Conditional: HasCondition(element)))
                .Where(entry => entry.Value is not null)
                .ToList();

            if (!values.Any(entry => !entry.Conditional && Same(entry.Value, expected.Value)))
            {
                findings.Add(Block(
                    props.Path, $"must set {expected.Key} to {expected.Value} for '{project.Path}'"));
            }

            foreach (var value in values.Select(entry => entry.Value).Where(value => !Same(value, expected.Value)))
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
        => element.AncestorsAndSelf().Any(node => !string.IsNullOrWhiteSpace(node.Attribute("Condition")?.Value));

    private static void RejectWeakening(DotNetFile file, List<Finding> findings)
    {
        foreach (var element in file.Root.Descendants())
        {
            var name = element.Name.LocalName;
            var value = DotNetRepository.Value(element);
            if ((name is "RunAnalyzers" or "RunAnalyzersDuringBuild" or "RunAnalyzersDuringLiveAnalysis"
                    or "CodeAnalysisTreatWarningsAsErrors" && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                || (name == "WarningLevel" && int.TryParse(value, out var level) && level < 4)
                || (name.StartsWith("AnalysisMode", StringComparison.Ordinal) && string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(Block(file.Path, $"{name} = {value} weakens analyzer coverage"));
            }
        }
    }

    private static void RejectUnresolvedImports(DotNetFile file, List<Finding> findings)
    {
        foreach (var import in DotNetRepository.Elements(file, "Import"))
        {
            var path = import.Attribute("Project")?.Value ?? string.Empty;
            if (path.Length == 0 || Path.IsPathRooted(path)
                || path.Contains("http:", StringComparison.OrdinalIgnoreCase)
                || path.Contains("https:", StringComparison.OrdinalIgnoreCase)
                || path.Contains("$(", StringComparison.Ordinal)
                    && !path.StartsWith("$(MSBuildThisFileDirectory)", StringComparison.Ordinal))
            {
                findings.Add(Block(file.Path, $"Import '{path}' cannot be resolved to tracked local evidence"));
            }
        }
    }

    internal static List<DotNetFile> ReadLocalImports(CheckContext context, DotNetFile file, List<Finding> findings)
    {
        var files = new List<DotNetFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { file.Path };
        var pending = new Queue<DotNetFile>();
        pending.Enqueue(file);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var import in DotNetRepository.Elements(current, "Import"))
            {
                var declared = import.Attribute("Project")?.Value ?? string.Empty;
                if (HasCondition(import))
                {
                    findings.Add(Block(current.Path, $"conditional Import '{declared}' cannot establish unconditional policy"));
                    continue;
                }
                var relative = declared.Replace("$(MSBuildThisFileDirectory)", string.Empty, StringComparison.Ordinal);
                if (relative.Length == 0 || relative.Contains("$(", StringComparison.Ordinal)
                    || Path.IsPathRooted(relative) || relative.Contains('*'))
                {
                    continue;
                }

                var path = DotNetRepository.NormalizeRelative(current.Path, relative);
                if (!seen.Add(path))
                {
                    continue;
                }

                var entry = context.Repository.TrackedEntries.FirstOrDefault(item => item.Path == path)
                    ?? context.Repository.Ancestors(Path.GetFileName(path)).FirstOrDefault(item => item.Path == path);
                if (entry is null)
                {
                    findings.Add(Block(current.Path, $"Import '{declared}' is not tracked local evidence"));
                    continue;
                }

                var (read, failure) = DotNetRepository.Read(context.Repository, entry);
                if (read is null)
                {
                    findings.Add(Block(current.Path, $"Import '{declared}' cannot be read: {failure}"));
                    continue;
                }

                files.Add(read);
                pending.Enqueue(read);
            }
        }

        return files;
    }

    private static bool Same(string? value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
