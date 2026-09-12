using Harness.Repository;
using Harness.Structure;

namespace Harness.Languages;

/// <summary>
/// Everything a language has to supply for the structural checks to run on it. The checks
/// know nothing about syntax: they receive a graph and report on it. A second language is an
/// implementation of this, not a second copy of a check.
/// </summary>
internal interface ILanguageAnalyzer
{
    Language Language { get; }

    /// <summary>The node of the graph in this language, as the report names it: "file" or "package".</summary>
    string Unit { get; }

    /// <summary>Why the repository has nothing for this language to read.</summary>
    string NothingToAnalyze { get; }

    /// <summary>Tracked files read by name to resolve references (`go.mod`), declared as evidence by the checks.</summary>
    IReadOnlyList<string> NamedEvidence { get; }

    (SourceGraph? Graph, string? Failure) ReadGraph(IRepository repository);
}
