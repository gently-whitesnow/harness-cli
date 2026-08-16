using Harness.Git;

namespace Harness.Checks.Maintainability;

/// <param name="Name">Exactly what is counted, so the report cannot promise more than it measured.</param>
/// <param name="ComparisonPoint">The value a measurement is reported against; never a threshold that fails a run.</param>
internal sealed record MaintainabilityMetric(string Name, int ComparisonPoint);

/// <param name="Location">Repository-relative path, with a line when the subject has one.</param>
internal sealed record Measurement(MaintainabilityMetric Metric, int Value, string Subject, string Location);

/// <summary>
/// Measures C# hotspots lexically and reports them as evidence. Every finding names the
/// metric, the measured value, the comparison point, the subject and where to read it, so
/// an agent can decide whether a refactor is warranted without re-deriving the numbers.
/// Nothing here is blocking: approximate analysis does not get to impose architectural
/// taste on a repository.
/// </summary>
internal sealed class MaintainabilityCheck : IRepositoryCheck
{
    /// <summary>Enough of the worst subjects per metric to act on; the rest are counted, not listed.</summary>
    private const int ShownPerMetric = 5;

    private static readonly MaintainabilityMetric FileLines = new("file logical lines", 400);
    private static readonly MaintainabilityMetric TypeLines = new("type logical lines", 300);
    private static readonly MaintainabilityMetric MethodLines = new("method logical lines", 60);
    private static readonly MaintainabilityMetric Branches = new("lexical branch count", 12);
    private static readonly MaintainabilityMetric ConstructorParameters = new("constructor parameter count", 6);
    private static readonly MaintainabilityMetric PublicMembers = new("public declared members", 25);
    private static readonly MaintainabilityMetric ImportFanOut = new("using directive fan-out", 20);

    /// <summary>Report order, so a run of the same repository always reads the same way.</summary>
    private static readonly MaintainabilityMetric[] Metrics =
    [
        FileLines,
        TypeLines,
        MethodLines,
        Branches,
        ConstructorParameters,
        PublicMembers,
        ImportFanOut,
    ];

    /// <summary>File names the .NET ecosystem reserves for tool output rather than authored code.</summary>
    private static readonly string[] GeneratedSuffixes = [".g.cs", ".generated.cs", ".designer.cs"];

    /// <summary>
    /// The branching keywords counted, longest match first so `foreach` is not read as `for`.
    /// </summary>
    private static readonly string[] BranchKeywords =
        ["foreach", "while", "catch", "case", "when", "for", "if", "do"];

    public string Id => "maintainability.csharp";

    public string Group => "maintainability";

    public string Summary => "C# maintainability hotspots";

    public string Explanation => MaintainabilityExplanation.Text;

    public CheckEvaluation Evaluate(GitRepository repository)
    {
        var candidates = repository.TrackedEntries
            .Where(entry => entry.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(entry => !RepositoryLocations.IsGenerated(entry.Path))
            .Where(entry => !GeneratedSuffixes.Any(suffix =>
                entry.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        var measurements = new List<Measurement>();
        var analyzed = 0;

        foreach (var entry in candidates)
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                // Source the harness could not read is not evidence of anything.
                return CheckEvaluation.Incomplete(failure ?? $"Could not read '{entry.Path}'.");
            }

            if (IsGeneratedContent(text))
            {
                continue;
            }

            analyzed++;
            Measure(CSharpSource.Read(entry.Path, text), measurements);
        }

        return analyzed == 0
            ? CheckEvaluation.NotApplicable(
                "no tracked C# source outside generated and build-output locations")
            : CheckEvaluation.From(Report(measurements));
    }

    private static void Measure(CSharpSource source, List<Measurement> measurements)
    {
        var structure = CSharpStructureReader.Read(source);

        measurements.Add(new Measurement(FileLines, source.LogicalLines, source.Path, source.Path));
        measurements.Add(new Measurement(ImportFanOut, structure.UsingDirectives, source.Path, source.Path));

        foreach (var declaration in structure.Declarations)
        {
            var location = source.Path + ":" + declaration.FirstLine;
            var logicalLines = source.LogicalLinesBetween(declaration.FirstLine, declaration.LastLine);

            if (declaration.Kind == DeclarationKind.Type)
            {
                measurements.Add(new Measurement(TypeLines, logicalLines, declaration.Subject, location));
                measurements.Add(new Measurement(
                    PublicMembers, declaration.PublicMembers, declaration.Subject, location));
            }
            else
            {
                measurements.Add(new Measurement(MethodLines, logicalLines, declaration.Subject, location));
                measurements.Add(new Measurement(
                    Branches,
                    BranchCount(source.TextBetween(declaration.FirstLine, declaration.LastLine)),
                    declaration.Subject,
                    location));
            }

            // A primary constructor belongs to its type; a declared one to itself. Both are
            // the same measurement, so both are reported under the same name.
            if (declaration.ParameterCount >= 0)
            {
                measurements.Add(new Measurement(
                    ConstructorParameters, declaration.ParameterCount, declaration.Subject, location));
            }
        }
    }

    /// <summary>
    /// The worst subjects per metric, and an honest count of the ones left out. Bounded
    /// output is part of the contract: a report an agent cannot read is not evidence.
    /// </summary>
    private static IReadOnlyList<Finding> Report(List<Measurement> measurements)
    {
        var findings = new List<Finding>();

        foreach (var metric in Metrics)
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

    /// <summary>
    /// Counted branch tokens plus the entry path. The token set is fixed and documented:
    /// this is a lexical count of branching keywords and short-circuiting operators, not a
    /// control-flow graph.
    /// </summary>
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

    /// <summary>
    /// The marker generators agree on. Reading it from the first lines keeps the exclusion
    /// explainable: a file is excluded because it says it is generated, not because the
    /// harness guessed from its shape.
    /// </summary>
    private static bool IsGeneratedContent(string text)
        => text.Split('\n').Take(5).Any(line => line.Contains("<auto-generated", StringComparison.Ordinal));
}
