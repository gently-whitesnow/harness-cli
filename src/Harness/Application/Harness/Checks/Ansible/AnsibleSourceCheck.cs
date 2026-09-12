using Harness.Languages;
using Harness.Languages.Ansible;
using Harness.Repository;

namespace Harness.Checks.Ansible;

/// <summary>
/// What every check of the Ansible axis shares: its identity, the evidence it reads by name,
/// and the rule that without a marker there is nothing to judge. A check adds the judgement.
/// </summary>
internal abstract class AnsibleSourceCheck(IAnsibleSources sources, string group, string summary) : IRepositoryCheck
{
    protected IAnsibleSources Sources => sources;

    public string Id => Language.Ansible.Qualify(group);

    public string Group => group;

    public string Applicability => Language.Ansible.Key;

    public virtual IReadOnlyList<EvidenceFile> Evidence => AnsibleMarkers.Sources;

    public string Summary => $"{Language.Ansible.Name} {summary}";

    public abstract string Explanation { get; }

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (files, failure) = sources.Read(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        return sources.Markers(context.Repository).Count == 0
            ? CheckEvaluation.NotApplicable(IAnsibleSources.NothingToAnalyze)
            : Judge(context, files);
    }

    protected abstract CheckEvaluation Judge(CheckContext context, IReadOnlyList<AnsibleFile> files);
}
