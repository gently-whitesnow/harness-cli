using Harness.Repository;
using Harness.Versioning;

namespace Harness.Contracts.Commits;

internal interface ICommitIntegration
{
    (CommitHookStatus? Status, string? Failure) Inspect(
        IRepository repository,
        CommitSettings settings,
        string template,
        HarnessVersion? pin);

    (CommitHookStatus? Status, string? Failure) Install(
        IRepository repository,
        CommitSettings settings,
        string template,
        HarnessVersion? pin);
}
