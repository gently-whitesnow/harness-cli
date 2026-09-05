using Harness.Repository;

namespace Harness.Contracts.Commits;

internal interface ICommitIntegration
{
    (CommitHookStatus? Status, string? Failure) Inspect(
        IRepository repository,
        CommitSettings settings,
        string template);

    (CommitHookStatus? Status, string? Failure) Install(
        IRepository repository,
        CommitSettings settings,
        string template);
}
