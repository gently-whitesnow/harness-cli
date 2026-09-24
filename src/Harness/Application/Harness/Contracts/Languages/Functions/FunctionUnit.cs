namespace Harness.Languages.Functions;

internal sealed record FunctionUnit(
    string Path, string Name, int Line, int OwnLines, int LogicalLines, int LargestNestedLines, int CognitiveComplexity);

internal interface IFunctionSources
{
    Harness.Languages.Language Language { get; }

    string NothingToAnalyze { get; }

    (IReadOnlyList<FunctionUnit> Units, string? Failure) Read(Harness.Repository.IRepository repository);
}
