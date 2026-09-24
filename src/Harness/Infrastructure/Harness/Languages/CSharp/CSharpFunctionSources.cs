using Harness.Infrastructure.Languages.Functions;
using Harness.Languages;
using Harness.Languages.CSharp;
using Harness.Languages.Functions;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.CSharp;

internal sealed class CSharpFunctionSources(ICSharpSources sources) : IFunctionSources
{
    public Language Language => Language.CSharp;

    public string NothingToAnalyze => ICSharpSources.NothingToAnalyze;

    public (IReadOnlyList<FunctionUnit> Units, string? Failure) Read(IRepository repository)
    {
        var (files, failure) = sources.Read(repository);
        return failure is not null
            ? ([], failure)
            : (files.SelectMany(file => FunctionScanner.Read(file.Path, file.Source.Masked, false)).ToList(), null);
    }
}
