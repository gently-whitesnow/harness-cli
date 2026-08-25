namespace Harness.Checks;

/// <summary>
/// A tracked file a check looks up by name and reports as missing: a file name such as
/// `Directory.Packages.props`, or a `*.ext` pattern when any file of that shape answers the
/// question. Naming it is what lets a run tell "never written" from "never staged" (ADR-0026).
/// </summary>
internal sealed record EvidenceFile(string Name)
{
    public bool IsPattern => Name.StartsWith('*');

    // A pattern compares its extension the way the toolchains behind it do, ignoring case;
    // a name is compared exactly, because that is how the tools reading it resolve one.
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
