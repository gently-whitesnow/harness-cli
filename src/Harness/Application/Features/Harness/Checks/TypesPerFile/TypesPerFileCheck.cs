using Harness.Languages.CSharp;

namespace Harness.Checks.TypesPerFile;

internal sealed class TypesPerFileCheck(ICSharpSources sources)
    : CSharpSourceCheck(
        sources,
        "types-per-file",
        "one top-level C# class or record per file",
        TypesPerFileExplanation.Text)
{
    protected override CheckEvaluation Evaluate(CheckContext context, IReadOnlyList<CSharpFile> files)
    {
        var findings = new List<Finding>();
        foreach (var file in files)
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
