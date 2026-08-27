using Harness.Languages;

namespace Harness.Checks;

/// <summary>Supplies the shared language identity and source evidence contract for structural checks.</summary>
internal abstract class LanguageAnalyzerCheck(
    ILanguageAnalyzer analyzer,
    string group,
    string summary) : IRepositoryCheck
{
    protected ILanguageAnalyzer Analyzer => analyzer;

    public string Id => analyzer.Language.Qualify(group);

    public string Group => group;

    public string Applicability => analyzer.Language.Key;

    public IReadOnlyList<EvidenceFile> Evidence => [];

    public string Summary => $"{analyzer.Language.Name} {summary}";

    public abstract string Explanation { get; }

    public abstract CheckEvaluation Evaluate(CheckContext context);
}
