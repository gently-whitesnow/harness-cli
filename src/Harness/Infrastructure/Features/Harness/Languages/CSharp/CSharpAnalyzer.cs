using Harness.Repository;
using Harness.Structure;

namespace Harness.Languages.CSharp;

/// <summary>
/// The C# side of the structural checks. It reads tracked source, never a build output and
/// never the compiler: what it can prove, it proves from the text the repository ships.
/// </summary>
internal sealed class CSharpAnalyzer(CSharpSources sources) : ILanguageAnalyzer
{
    public Language Language => Language.CSharp;

    public string NothingToAnalyze => ICSharpSources.NothingToAnalyze;

    public (SourceGraph? Graph, string? Failure) ReadGraph(IRepository repository)
    {
        var (files, failure) = sources.Read(repository);
        return failure is not null ? (null, failure) : (CSharpGraphBuilder.Build(files), null);
    }

}
