using Harness.Config;
using Harness.Languages;
using Harness.Languages.CSharp;

namespace Harness.Checks.TypesPerFile;

internal sealed class TypesPerFileCheck(CSharpSources sources) : IRepositoryCheck
{
    public string Id => Language.CSharp.Qualify("types-per-file");

    public string Group => "types-per-file";

    public string Applicability => Language.CSharp.Key;

    public string Summary => "one top-level C# class or record per file";

    public string Explanation => TypesPerFileExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (files, failure) = sources.Read(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (files.Count == 0)
        {
            return CheckEvaluation.NotApplicable(CSharpSources.NothingToAnalyze);
        }

        var analyzed = files
            .Where(file => !OverrideResolution.Disables(context.Config, Id, file.Path))
            .ToList();
        if (analyzed.Count == 0)
        {
            return CheckEvaluation.NotApplicable(OverrideResolution.EverythingExcluded);
        }

        var findings = new List<Finding>();
        foreach (var file in analyzed)
        {
            var declarations = file.Types
                .Where(declaration => !declaration.IsNestedType)
                .Where(declaration => declaration.TypeForm is TypeForm.Class or TypeForm.Record)
                .ToList();

            if (declarations.Count > 1)
            {
                findings.Add(new Finding(
                    FindingSeverity.Blocking,
                    file.Path,
                    $"contains {declarations.Count} top-level classes or records "
                        + $"({string.Join(", ", declarations.Select(declaration => declaration.Subject))}); "
                        + "keep at most one in each authored C# file"));
            }
        }

        return CheckEvaluation.From(findings);
    }
}
