using Harness.Checks.Metrics;
using Harness.Config;
using Harness.Languages.CSharp;
using Harness.Versioning;

namespace Harness.Checks.Maintainability;

/// <summary>Reports versioned lexical C# hotspot measurements.</summary>
internal sealed class MaintainabilityCheck(CSharpSources sources)
    : CSharpSourceCheck(
        sources,
        "maintainability",
        "C# maintainability hotspots",
        MaintainabilityExplanation.Text)
{
    private const int ShownPerMetric = 5;
    private static readonly HarnessVersion ContextualWidthCountsRemovedIn = new(1, 6, 0);

    protected override CheckEvaluation Evaluate(CheckContext context, IReadOnlyList<CSharpFile> files)
    {
        var config = context.Config;
        var measuresContextualWidthCounts = config is not null
            && !config.TracksLatest
            && config.Version < ContextualWidthCountsRemovedIn;
        var metrics = new MetricSet(
            config?.Settings.Maintainability ?? MaintainabilitySettings.Default,
            measuresContextualWidthCounts);

        var measurements = new List<Measurement>();
        foreach (var file in files)
        {
            Measure(file, metrics, measurements);
        }

        var report = MetricReport.Exceeding(measurements, metrics.All, ShownPerMetric);
        return CheckEvaluation.From(report.Summary, detailedFindings: report.Detailed);
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
                    if (metrics.PublicMembers is not null)
                    {
                        measurements.Add(new Measurement(
                            metrics.PublicMembers, declaration.PublicMembers, declaration.Subject, location));
                    }
                    break;

                case DeclarationKind.Field:
                    break;

                default:
                    measurements.Add(new Measurement(
                        metrics.MethodLines, logicalLines, declaration.Subject, location));
                    break;
            }

            // Legacy pins count both constructor forms but exclude positional records,
            // whose parameter list is data shape.
            if (metrics.ConstructorParameters is not null
                && declaration.ParameterCount >= 0
                && declaration.TypeForm != TypeForm.Record)
            {
                measurements.Add(new Measurement(
                    metrics.ConstructorParameters, declaration.ParameterCount, declaration.Subject, location));
            }
        }
    }

    private sealed class MetricSet
    {
        public MetricSet(MaintainabilitySettings settings, bool measuresContextualWidthCounts)
        {
            FileLines = new("file logical lines", settings.FileLines);
            TypeLines = new("type logical lines", settings.TypeLines);
            MethodLines = new("method logical lines", settings.MethodLines);
            ConstructorParameters = measuresContextualWidthCounts
                ? new Metric("constructor parameter count", settings.ConstructorParameters)
                : null;
            PublicMembers = measuresContextualWidthCounts
                ? new Metric("public declared members", settings.PublicMembers)
                : null;
            var all = new List<Metric>
            {
                FileLines,
                TypeLines,
                MethodLines,
            };
            if (ConstructorParameters is not null)
            {
                all.Add(ConstructorParameters);
            }

            if (PublicMembers is not null)
            {
                all.Add(PublicMembers);
            }

            All = all;
        }

        public Metric FileLines { get; }
        public Metric TypeLines { get; }
        public Metric MethodLines { get; }
        public Metric? ConstructorParameters { get; }
        public Metric? PublicMembers { get; }
        public IReadOnlyList<Metric> All { get; }
    }
}
