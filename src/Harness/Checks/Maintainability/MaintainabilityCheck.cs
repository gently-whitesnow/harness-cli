using Harness.Checks.CSharp;
using Harness.Config;
using Harness.Git;

namespace Harness.Checks.Maintainability;

/// <summary>
/// Measures C# hotspots lexically and reports them as evidence. Every finding names the
/// metric, the measured value, the comparison point, the subject and where to read it, so
/// an agent can decide whether a refactor is warranted without re-deriving the numbers.
/// Nothing here is blocking: approximate analysis does not get to impose architectural
/// taste on a repository.
/// </summary>
internal sealed class MaintainabilityCheck : IRepositoryCheck
{
    private const int ShownPerMetric = 5;

    // Longest first so `foreach` is not read as `for`.
    private static readonly string[] BranchKeywords =
        ["foreach", "while", "catch", "case", "when", "for", "if", "do"];

    public string Id => "maintainability.csharp";

    public string Group => "maintainability";

    public string Applicability => "csharp";

    public string Summary => "C# maintainability hotspots";

    public string Explanation => MaintainabilityExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var repository = context.Repository;
        var metrics = new MetricSet(
            context.Config?.Settings.Maintainability ?? MaintainabilitySettings.Default);

        var (sources, failure) = CSharpSources.Discover(repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (sources.Count == 0)
        {
            return CheckEvaluation.NotApplicable(CSharpSources.NothingToAnalyze);
        }

        var measurements = new List<Measurement>();
        foreach (var source in sources)
        {
            Measure(source, metrics, measurements);
        }

        return CheckEvaluation.From(Report(measurements, metrics.All));
    }

    private static void Measure(CSharpSource source, MetricSet metrics, List<Measurement> measurements)
    {
        var structure = CSharpStructureReader.Read(source);

        measurements.Add(new Measurement(metrics.FileLines, source.LogicalLines, source.Path, source.Path));
        measurements.Add(new Measurement(metrics.ImportFanOut, structure.UsingDirectives, source.Path, source.Path));

        foreach (var declaration in structure.Declarations)
        {
            var location = source.Path + ":" + declaration.FirstLine;
            var logicalLines = source.LogicalLinesBetween(declaration.FirstLine, declaration.LastLine);

            if (declaration.Kind == DeclarationKind.Type)
            {
                measurements.Add(new Measurement(metrics.TypeLines, logicalLines, declaration.Subject, location));
                measurements.Add(new Measurement(
                    metrics.PublicMembers, declaration.PublicMembers, declaration.Subject, location));
            }
            else
            {
                measurements.Add(new Measurement(metrics.MethodLines, logicalLines, declaration.Subject, location));
                measurements.Add(new Measurement(
                    metrics.Branches,
                    BranchCount(source.TextBetween(declaration.FirstLine, declaration.LastLine)),
                    declaration.Subject,
                    location));
            }

            // A primary constructor belongs to its type; a declared one to itself. Both are
            // the same measurement, so both are reported under the same name.
            if (declaration.ParameterCount >= 0)
            {
                measurements.Add(new Measurement(
                    metrics.ConstructorParameters, declaration.ParameterCount, declaration.Subject, location));
            }
        }
    }

    private static IReadOnlyList<Finding> Report(
        List<Measurement> measurements,
        IReadOnlyList<MaintainabilityMetric> metrics)
    {
        var findings = new List<Finding>();

        foreach (var metric in metrics)
        {
            var exceeded = measurements
                .Where(measurement => ReferenceEquals(measurement.Metric, metric))
                .Where(measurement => measurement.Value > metric.ComparisonPoint)
                .OrderByDescending(measurement => measurement.Value)
                .ThenBy(measurement => measurement.Location, StringComparer.Ordinal)
                .ToList();

            foreach (var measurement in exceeded.Take(ShownPerMetric))
            {
                findings.Add(new Finding(
                    FindingSeverity.Advisory,
                    measurement.Location,
                    $"{metric.Name} {measurement.Value} exceeds the advisory comparison point "
                        + $"of {metric.ComparisonPoint} in {measurement.Subject}"));
            }

            if (exceeded.Count > ShownPerMetric)
            {
                findings.Add(new Finding(
                    FindingSeverity.Advisory,
                    exceeded[ShownPerMetric].Location,
                    $"{metric.Name}: {exceeded.Count} subjects exceed the advisory comparison point "
                        + $"of {metric.ComparisonPoint}; the {ShownPerMetric} largest are listed above"));
            }
        }

        return findings;
    }

    private static int BranchCount(ReadOnlySpan<char> text)
    {
        var count = 1;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            // `&&`, `||` and `??` each introduce a path; `&`, `|`, `?.` and `?:` do not.
            if (character is '&' or '|' or '?')
            {
                if (index + 1 < text.Length && text[index + 1] == character)
                {
                    count++;
                    index++;
                }

                continue;
            }

            if (!char.IsLetter(character) || (index > 0 && IsWordCharacter(text[index - 1])))
            {
                continue;
            }

            foreach (var keyword in BranchKeywords)
            {
                if (!text[index..].StartsWith(keyword))
                {
                    continue;
                }

                var after = index + keyword.Length;
                if (after < text.Length && IsWordCharacter(text[after]))
                {
                    continue;
                }

                count++;
                index = after - 1;
                break;
            }
        }

        return count;
    }

    private static bool IsWordCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '@';

    private sealed class MetricSet
    {
        public MetricSet(MaintainabilitySettings settings)
        {
            FileLines = new("file logical lines", settings.FileLines);
            TypeLines = new("type logical lines", settings.TypeLines);
            MethodLines = new("method logical lines", settings.MethodLines);
            Branches = new("lexical branch count", settings.Branches);
            ConstructorParameters = new("constructor parameter count", settings.ConstructorParameters);
            PublicMembers = new("public declared members", settings.PublicMembers);
            ImportFanOut = new("using directive fan-out", settings.ImportFanOut);
            All =
            [
                FileLines,
                TypeLines,
                MethodLines,
                Branches,
                ConstructorParameters,
                PublicMembers,
                ImportFanOut,
            ];
        }

        public MaintainabilityMetric FileLines { get; }
        public MaintainabilityMetric TypeLines { get; }
        public MaintainabilityMetric MethodLines { get; }
        public MaintainabilityMetric Branches { get; }
        public MaintainabilityMetric ConstructorParameters { get; }
        public MaintainabilityMetric PublicMembers { get; }
        public MaintainabilityMetric ImportFanOut { get; }
        public IReadOnlyList<MaintainabilityMetric> All { get; }
    }
}
