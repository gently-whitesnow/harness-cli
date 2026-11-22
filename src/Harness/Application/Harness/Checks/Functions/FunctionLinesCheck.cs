using Harness.Config;
using Harness.Languages.Functions;
using Harness.Repository;

namespace Harness.Checks.Functions;

internal sealed class FunctionLinesCheck(IFunctionSources sources) : IRepositoryCheck
{
    public string Id => sources.Language.Qualify("functions");
    public string Group => "functions";
    public string Applicability => sources.Language.Key;
    public IReadOnlyList<EvidenceFile> Evidence => [];
    public string Summary => $"{sources.Language.Name} function own-token limit";
    public string Explanation => $"""
        Rationale
          Long functions hide named steps of logic and make review and change harder.

        Rule
          Each function, method, local function and lambda is measured independently. Own
          tokens are lexical tokens from the signature to the closing brace, excluding comments,
          string literal contents, nested callable bodies and data literals. Top-level C#
          statements and Go main are measured too. Tests use the same limit.

          The limit is settings.{Id}.ownTokens (default {FunctionSettings.Default.OwnTokens}).
          Because lexical tokens ignore whitespace and line breaks, packing statements onto one
          line cannot evade the check. Change the limit only by editing the tracked frame. The
          reported cognitive number is a lexical estimate of branching and nesting, context only,
          never a gate.

        Remediation
          Extract a named step of domain logic. A private method in the same file suffices.
        """;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (units, failure) = sources.Read(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (units.Count == 0)
        {
            return CheckEvaluation.NotApplicable(sources.NothingToAnalyze);
        }

        var limit = context.Config?.Settings.FunctionsFor(sources.Language)?.OwnTokens ?? FunctionSettings.Default.OwnTokens;
        var findings = units.Where(unit => unit.OwnTokens > limit)
            .Select(unit => new Finding(FindingSeverity.Blocking, $"{unit.Path}:{unit.Line}",
                $"{unit.Name} owns {unit.OwnTokens} tokens (limit {limit}; own lines {unit.OwnLines}; "
                    + $"logical {unit.LogicalLines}; largest nested {unit.LargestNestedLines}; "
                    + $"cognitive ~{unit.CognitiveComplexity}); extract a named step of domain logic"))
            .ToList();
        return CheckEvaluation.From(findings);
    }
}
