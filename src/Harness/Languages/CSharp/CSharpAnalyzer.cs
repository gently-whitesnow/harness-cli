using Harness.Git;
using Harness.Structure;

namespace Harness.Languages.CSharp;

/// <summary>
/// The C# side of the structural checks. It reads tracked source, never a build output and
/// never the compiler: what it can prove, it proves from the text the repository ships.
/// </summary>
internal sealed class CSharpAnalyzer : ILanguageAnalyzer
{
    public Language Language => Language.CSharp;

    public string NothingToAnalyze => CSharpSources.NothingToAnalyze;

    public (SourceGraph? Graph, string? Failure) ReadGraph(GitRepository repository)
    {
        var (files, failure) = Read(repository);
        return files is null ? (null, failure) : (CSharpGraphBuilder.Build(files), null);
    }

    public (IReadOnlyList<TypeCohesion>? Types, string? Failure) ReadCohesion(GitRepository repository)
    {
        var (files, failure) = Read(repository);
        return files is null ? (null, failure) : (CSharpCohesionReader.Read(files), null);
    }

    private static (IReadOnlyList<CSharpFile>? Files, string? Failure) Read(GitRepository repository)
    {
        var (sources, failure) = CSharpSources.Discover(repository);
        if (failure is not null)
        {
            return (null, failure);
        }

        return (sources.Select(source => new CSharpFile(source, CSharpStructureReader.Read(source))).ToList(), null);
    }
}
