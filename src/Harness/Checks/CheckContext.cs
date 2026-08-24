using Harness.Config;
using Harness.Git;

namespace Harness.Checks;

internal sealed class CheckContext(GitRepository repository, HarnessConfig? config, string? configFailure)
{
    public GitRepository Repository { get; } = repository;

    public HarnessConfig? Config { get; } = config;

    public string? ConfigFailure { get; } = configFailure;
}
