using Harness.Languages;
using Harness.Languages.CSharp;

namespace Harness.Checks.TypesPerFile;

internal sealed class TypesPerFileCheck : IRepositoryCheck
{
    public string Id => Language.CSharp.Qualify("types-per-file");

    public string Group => "types-per-file";

    public string Applicability => Language.CSharp.Key;

    public string Summary => "one top-level C# class or record per file";

    public string Explanation => TypesPerFileExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (sources, failure) = CSharpSources.Discover(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (sources.Count == 0)
        {
            return CheckEvaluation.NotApplicable(CSharpSources.NothingToAnalyze);
        }

        var findings = new List<Finding>();
        foreach (var source in sources)
        {
            var declarations = CSharpStructureReader.Read(source).Declarations
                .Where(declaration => declaration.Kind == DeclarationKind.Type)
                .Where(declaration => !declaration.IsNestedType)
                .Where(declaration => declaration.TypeForm is TypeForm.Class or TypeForm.Record)
                .ToList();

            if (declarations.Count > 1)
            {
                findings.Add(new Finding(
                    FindingSeverity.Blocking,
                    source.Path,
                    $"contains {declarations.Count} top-level classes or records "
                        + $"({string.Join(", ", declarations.Select(declaration => declaration.Subject))}); "
                        + "keep at most one in each authored C# file"));
            }
        }

        return CheckEvaluation.From(findings);
    }
}
