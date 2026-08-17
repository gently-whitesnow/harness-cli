namespace Harness.Structure;

/// <summary>
/// How much a single dependency edge is worth as evidence. The harness may only fail a run
/// on what it can prove, so this grade — not the metric it feeds — decides whether a finding
/// is allowed to be blocking.
/// </summary>
internal enum EvidenceGrade
{
    /// <summary>
    /// The reference stands in a declaration position — a construction, a base list, the
    /// type of a parameter, field, property or return value, a generic argument, an
    /// attribute, a `typeof`, a `catch` or a pattern — and exactly one declaration in the
    /// repository carries that name. Nothing else in the language can be written that way.
    /// </summary>
    Proven,

    /// <summary>
    /// The name matches a declaration, but the position does not prove it is that
    /// declaration: it may be a member access, an argument, a local, or one of several
    /// same-named types the reader could not tell apart.
    /// </summary>
    Inferred,
}
