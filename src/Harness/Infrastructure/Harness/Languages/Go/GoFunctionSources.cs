using Harness.Infrastructure.Languages.Functions;
using Harness.Languages;
using Harness.Languages.Functions;
using Harness.Languages.Go;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.Go;

internal sealed class GoFunctionSources(IGoSources sources) : IFunctionSources
{
    public Language Language => Language.Go;

    public string NothingToAnalyze => IGoSources.NothingToAnalyze;

    public (IReadOnlyList<FunctionUnit> Units, string? Failure) Read(IRepository repository)
    {
        var (files, failure) = sources.Read(repository);
        return failure is not null
            ? ([], failure)
            : (files.SelectMany(file => FunctionScanner.Read(file.Path, file.Masked, true)).ToList(), null);
    }
}
