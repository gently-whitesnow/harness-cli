using Harness.Languages;
using Harness.Languages.CSharp;

namespace Harness.Checks;

/// <summary>Handles the shared tracked-source and applicability contract for C# checks.</summary>
internal abstract class CSharpSourceCheck(
    ICSharpSources sources,
    string group,
    string summary,
    string explanation) : IRepositoryCheck
{
    public string Id => Language.CSharp.Qualify(group);

    public string Group => group;

    public string Applicability => Language.CSharp.Key;

    public IReadOnlyList<EvidenceFile> Evidence => [];

    public string Summary => summary;

    public string Explanation => explanation;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (files, failure) = sources.Read(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        return files.Count == 0
            ? CheckEvaluation.NotApplicable(ICSharpSources.NothingToAnalyze)
            : Evaluate(context, files);
    }

    protected abstract CheckEvaluation Evaluate(CheckContext context, IReadOnlyList<CSharpFile> files);
}
