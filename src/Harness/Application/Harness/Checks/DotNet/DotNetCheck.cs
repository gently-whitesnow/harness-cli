using Harness.Repository;

namespace Harness.Checks.DotNet;

/// <summary>
/// What every .NET repository policy shares: the same applicability, the same first question
/// — are there tracked SDK-style projects at all — and the same answer when there are none.
/// A repository without them is not applicable, never a pass.
/// </summary>
internal abstract class DotNetCheck : IRepositoryCheck
{
    public abstract string Id { get; }

    public abstract string Group { get; }

    public string Applicability => "dotnet";

    public IReadOnlyList<EvidenceFile> Evidence => [.. DotNetRepository.ProjectFiles, .. PolicyFiles];

    /// <summary>The named files this policy reads on top of the projects it judges.</summary>
    protected abstract IReadOnlyList<EvidenceFile> PolicyFiles { get; }

    public abstract string Summary { get; }

    public abstract string Explanation { get; }

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (projects, failure) = DotNetRepository.ReadProjects(context);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        var nonSdk = new List<Finding>();
        foreach (var entry in DotNetRepository.ProjectFiles.SelectMany(context.Tracked))
        {
            var (file, readFailure) = DotNetRepository.Read(context.Repository, entry);
            if (readFailure is not null)
            {
                return CheckEvaluation.Incomplete(readFailure);
            }

            if (file is not null && !DotNetRepository.IsSdkStyle(file))
            {
                nonSdk.Add(Block(entry.Path, "non-SDK-style project cannot be measured by the .NET frame"));
            }
        }

        if (nonSdk.Count > 0)
        {
            return CheckEvaluation.From(nonSdk);
        }

        return projects.Count == 0
            ? CheckEvaluation.NotApplicable("no tracked SDK-style .NET projects were found")
            : Inspect(context, projects);
    }

    protected abstract CheckEvaluation Inspect(CheckContext context, IReadOnlyList<DotNetFile> projects);

    protected static Finding Block(string location, string message)
        => new(FindingSeverity.Blocking, location, message);
}
