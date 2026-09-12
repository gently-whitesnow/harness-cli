using Harness.Repository;

namespace Harness.Languages.Ansible;

/// <summary>
/// What the Ansible axis supplies to its checks: the tracked YAML and Jinja templates read
/// once, the markers that made the repository an Ansible one, and the tracked paths under
/// `roles/`. Without a marker there is nothing to judge, and every check says so.
/// </summary>
internal interface IAnsibleSources
{
    const string NothingToAnalyze = "no Ansible marker in the index (" + AnsibleMarkers.Description + ")";

    (IReadOnlyList<AnsibleFile> Files, string? Failure) Read(IRepository repository);

    IReadOnlyList<string> Markers(IRepository repository);

    /// <summary>Every tracked path under `roles/` the readers look at, whatever its suffix.</summary>
    IReadOnlyList<string> RolePaths(IRepository repository);
}
