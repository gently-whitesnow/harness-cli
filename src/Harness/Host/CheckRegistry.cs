using Harness.Checks;
using Harness.Checks.Ansible;
using Harness.Checks.Architecture;
using Harness.Checks.Commits;
using Harness.Checks.Complexity;
using Harness.Checks.Dependencies;
using Harness.Checks.DotNet;
using Harness.Checks.Duplication;
using Harness.Checks.Functions;
using Harness.Checks.LintSuppressions;
using Harness.Checks.TypesPerFile;
using Harness.Checks.Verify;
using Harness.Git;
using Harness.Infrastructure.Languages.Ansible;
using Harness.Infrastructure.Languages.CSharp;
using Harness.Infrastructure.Languages.Go;
using Harness.Infrastructure.Languages.TypeScript;
using Harness.Languages;

namespace Harness.Host;

/// <summary>The checks this version of the harness ships, in execution order.</summary>
internal static class CheckRegistry
{
    private static readonly CSharpSources CSharp = new();

    private static readonly GoSources Go = new();

    private static readonly AnsibleSources Ansible = new();

    public static readonly ICommitIntegration CommitIntegration = new CommitHookSetup();

    /// <summary>The analyzers whose graph is measured as a DSM; the Ansible role graph is judged for cycles only.</summary>
    public static readonly IReadOnlyList<ILanguageAnalyzer> LanguageAnalyzers =
        [new CSharpAnalyzer(CSharp), new TypeScriptAnalyzer(), new GoAnalyzer(Go)];

    private static readonly ILanguageAnalyzer AnsibleAnalyzer = new AnsibleAnalyzer(Ansible);

    public static readonly IReadOnlyList<IRepositoryCheck> All = Shipped();

    private static IReadOnlyList<IRepositoryCheck> Shipped()
    {
        var csharpAnalyzer = LanguageAnalyzers.Single(analyzer => analyzer.Language == Language.CSharp);

        return
        [
            new HarnessConfigCheck(),
            new HarnessCoverageCheck(() => CheckCatalog.Describe(All)),

            .. SlicedDotNetShapeCheck.Rules.Keys.Select(rule => new SlicedDotNetShapeCheck(csharpAnalyzer, rule)),
            .. LanguageAnalyzers.Select(analyzer => new ComplexityCheck(analyzer)),

            new DocumentationPolicyCheck(),
            new AdrShapeCheck(),
            new CommitSetupCheck(CommitIntegration),
            new TypesPerFileCheck(CSharp),
            new DependenciesCheck(csharpAnalyzer),
            new DependenciesCheck(LanguageAnalyzers.Single(analyzer => analyzer.Language == Language.TypeScript)),
            new DuplicationCheck(new CSharpNormalizedSources(CSharp)),
            new DuplicationCheck(new TypeScriptNormalizedSources()),
            new DuplicationCheck(new GoNormalizedSources(Go)),
            new FunctionLinesCheck(new CSharpFunctionSources(CSharp)),
            new FunctionLinesCheck(new GoFunctionSources(Go)),
            new LintSuppressionsCheck(Go),
            new AnsibleLintSuppressionsCheck(Ansible),
            new DependenciesCheck(AnsibleAnalyzer),

            new BuildPropertiesCheck(),
            new CentralPackagesCheck(),
            new SolutionFormatCheck(),
            new EditorConfigCheck(),
            new WarningSuppressionsCheck(),

            new VerifyEntrypointCheck(),
        ];
    }

}
