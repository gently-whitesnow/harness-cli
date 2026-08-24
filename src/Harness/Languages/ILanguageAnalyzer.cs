using Harness.Git;
using Harness.Structure;

namespace Harness.Languages;

/// <summary>
/// Everything a language has to supply for the structural checks to run on it. The checks
/// know nothing about syntax: they receive a graph and a set of member groups, and report on
/// those. A second language is an implementation of this, not a second copy of a check.
/// </summary>
internal interface ILanguageAnalyzer
{
    Language Language { get; }

    /// <summary>Why the repository has nothing for this language to read.</summary>
    string NothingToAnalyze { get; }

    (SourceGraph? Graph, string? Failure) ReadGraph(GitRepository repository);

    (IReadOnlyList<TypeCohesion>? Types, string? Failure) ReadCohesion(GitRepository repository);
}
