using Harness.Checks.Metrics;
using Harness.Config;
using Harness.Languages;
using Harness.Languages.CSharp;

namespace Harness.Checks.Maintainability;

/// <summary>
/// Measures C# hotspots lexically and reports them as evidence. Every finding names the
/// metric, the measured value, the comparison point, the subject and where to read it, so
/// an agent can decide whether a refactor is warranted without re-deriving the numbers.
/// Nothing here is blocking: approximate analysis does not get to impose architectural
/// taste on a repository.
/// </summary>
internal sealed class MaintainabilityCheck(CSharpSources sources) : IRepositoryCheck
{
    private const int ShownPerMetric = 5;

    // Longest first so `foreach` is not read as `for`.
    private static readonly string[] BranchKeywords =
        ["foreach", "while", "catch", "case", "when", "for", "if", "do"];

    public string Id => Language.CSharp.Qualify("maintainability");

    public string Group => "maintainability";

    public string Applicability => Language.CSharp.Key;

    public string Summary => "C# maintainability hotspots";

    public string Explanation => MaintainabilityExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (files, failure) = sources.Read(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (files.Count == 0)
        {
            return CheckEvaluation.NotApplicable(CSharpSources.NothingToAnalyze);
        }

        var analyzed = files
            .Where(file => !OverrideResolution.Disables(context.Config, Id, file.Source.Path))
            .ToList();
        if (analyzed.Count == 0)
        {
            return CheckEvaluation.NotApplicable(OverrideResolution.EverythingExcluded);
        }

        var reference = new MetricSet(
            context.Config?.Settings.Maintainability ?? MaintainabilitySettings.Default);
        var sets = new Dictionary<MaintainabilitySettings, MetricSet>();
        var measurements = new List<Measurement>();
        foreach (var file in analyzed)
        {
            Measure(file, MetricsFor(context, file, reference, sets), measurements);
        }

        return CheckEvaluation.From(MetricReport.Exceeding(measurements, reference.All, ShownPerMetric));
    }

    /// <summary>Files sharing effective settings share one metric set, override or not.</summary>
    private MetricSet MetricsFor(
        CheckContext context,
        CSharpFile file,
        MetricSet reference,
        Dictionary<MaintainabilitySettings, MetricSet> sets)
    {
        var settings = OverrideResolution.MaintainabilityFor(context.Config, Id, file.Source.Path);
        if (settings == reference.Settings)
        {
            return reference;
        }

        if (!sets.TryGetValue(settings, out var metrics))
        {
            sets[settings] = metrics = new MetricSet(settings);
        }

        return metrics;
    }

    private static void Measure(CSharpFile file, MetricSet metrics, List<Measurement> measurements)
    {
        var source = file.Source;
        measurements.Add(new Measurement(metrics.FileLines, source.LogicalLines, source.Path, source.Path));

        foreach (var declaration in file.Structure.Declarations)
        {
            var location = source.Path + ":" + declaration.FirstLine;
            var logicalLines = source.LogicalLinesBetween(declaration.FirstLine, declaration.LastLine);

            switch (declaration.Kind)
            {
                case DeclarationKind.Type:
                    measurements.Add(new Measurement(
                        metrics.TypeLines, logicalLines, declaration.Subject, location));
                    measurements.Add(new Measurement(
                        metrics.PublicMembers, declaration.PublicMembers, declaration.Subject, location));
                    break;

                case DeclarationKind.Field:
                    break;

                default:
                    measurements.Add(new Measurement(
                        metrics.MethodLines, logicalLines, declaration.Subject, location));
                    measurements.Add(new Measurement(
                        metrics.Branches,
                        BranchCount(source.TextBetween(declaration.FirstLine, declaration.LastLine)),
                        declaration.Subject,
                        location));
                    break;
            }

            // A primary constructor belongs to its type; a declared one to itself. Both are
            // the same measurement, so both are reported under the same name. A positional
            // record is excluded: its parameter list is the shape of the data it holds, not
            // a list of collaborators the type had to be handed.
            if (declaration.ParameterCount >= 0 && declaration.TypeForm != TypeForm.Record)
            {
                measurements.Add(new Measurement(
                    metrics.ConstructorParameters, declaration.ParameterCount, declaration.Subject, location));
            }
        }
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
            Settings = settings;
            FileLines = new("file logical lines", settings.FileLines);
            TypeLines = new("type logical lines", settings.TypeLines);
            MethodLines = new("method logical lines", settings.MethodLines);
            Branches = new("lexical branch count", settings.Branches);
            ConstructorParameters = new("constructor parameter count", settings.ConstructorParameters);
            PublicMembers = new("public declared members", settings.PublicMembers);
            All =
            [
                FileLines,
                TypeLines,
                MethodLines,
                Branches,
                ConstructorParameters,
                PublicMembers,
            ];
        }

        public MaintainabilitySettings Settings { get; }

        public Metric FileLines { get; }
        public Metric TypeLines { get; }
        public Metric MethodLines { get; }
        public Metric Branches { get; }
        public Metric ConstructorParameters { get; }
        public Metric PublicMembers { get; }
        public IReadOnlyList<Metric> All { get; }
    }
}
