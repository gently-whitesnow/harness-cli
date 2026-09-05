namespace Harness.Checks.Frame;

/// <summary>
/// One question of the frame as data: the answer key, how the report names its subject, and
/// the few rules that differ between questions. The questions differ in words, not in
/// behaviour, so they are rows of a table rather than eight classes.
/// </summary>
internal sealed record FrameQuestion(
    string Key,
    string Subject,
    string Summary,
    string Explanation)
{
    /// <summary>A positive answer must carry `paths`; presence without an address is incomplete.</summary>
    public bool RequiresLocation { get; init; }

    /// <summary>A test suite is addressed by the project that runs it, never by its files.</summary>
    public bool AddressesTestProjects { get; init; }

    /// <summary>The question cannot be answered `applicable: false`; every repository can own this.</summary>
    public bool AppliesToEveryRepository { get; init; }
}
