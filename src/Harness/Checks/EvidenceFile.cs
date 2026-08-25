namespace Harness.Checks;

/// <summary>
/// A tracked file a check looks up by name and whose absence it reports: either a file name
/// such as `Directory.Packages.props`, or a `*.ext` pattern such as `*.slnx` when any file of
/// that shape answers the question.
/// </summary>
/// <remarks>
/// Naming it is what lets a run tell "never written" apart from "written and never staged".
/// The declaration belongs to the check rather than to a finding, so an outcome that carries
/// no finding at all — an unreadable frame, an analysis that found nothing applicable — is
/// explained the same way, and so there is no construction site at which to forget it.
/// </remarks>
internal sealed record EvidenceFile(string Name)
{
    /// <summary>Whether this evidence names any file at all rather than one file.</summary>
    public bool IsPattern => Name.StartsWith('*');

    /// <summary>
    /// Whether a repository-relative path is a file this evidence names. A pattern compares
    /// its extension the way the file systems and toolchains behind it do, ignoring case; a
    /// name is compared exactly, because that is how the tools reading it resolve one.
    /// </summary>
    public bool Matches(string path)
        => IsPattern
            ? path.EndsWith(Name[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(FileNameOf(path), Name, StringComparison.Ordinal);

    private static string FileNameOf(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }
}
