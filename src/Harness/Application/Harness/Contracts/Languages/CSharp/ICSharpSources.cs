using Harness.Repository;

namespace Harness.Languages.CSharp;

internal interface ICSharpSources
{
    const string NothingToAnalyze =
        "no tracked C# source outside generated and build-output locations";

    (IReadOnlyList<CSharpFile> Files, string? Failure) Read(IRepository repository);
}
