using Harness.Languages;
using Harness.Languages.CSharp;
using Harness.Repository;
using Harness.Structure;

namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>
/// The C# side of the structural checks. It reads tracked source, never a build output and
/// never the compiler: what it can prove, it proves from the text the repository ships.
/// </summary>
internal sealed class CSharpAnalyzer(CSharpSources sources) : ILanguageAnalyzer
{
    private IRepository? read;
    private (SourceGraph? Graph, string? Failure) result;

    public Language Language => Language.CSharp;

    public string Unit => "file";

    public string NothingToAnalyze => ICSharpSources.NothingToAnalyze;

    public (SourceGraph? Graph, string? Failure) ReadGraph(IRepository repository)
    {
        // Like CSharpSources, cache one immutable repository snapshot shared by this run.
        if (!ReferenceEquals(read, repository))
        {
            var (files, failure) = sources.Read(repository);
            result = failure is not null
                ? (null, failure)
                : (CSharpGraphBuilder.Build(files, sources.MarkedGenerated(repository)), null);
            read = repository;
        }
        return result;
    }

}
