using Harness.Checks;
using Harness.Checks.Ansible;
using Harness.Checks.Architecture;
using Harness.Checks.Comments;
using Harness.Checks.Commits;
using Harness.Checks.Complexity;
using Harness.Checks.Dependencies;
using Harness.Checks.DotNet;
using Harness.Checks.Duplication;
using Harness.Checks.Frame;
using Harness.Checks.Functions;
using Harness.Checks.LintSuppressions;
using Harness.Checks.TypesPerFile;
using Harness.Git;
using Harness.Infrastructure.Languages.Ansible;
using Harness.Infrastructure.Languages.CSharp;
using Harness.Infrastructure.Languages.Go;
using Harness.Infrastructure.Languages.TypeScript;
using Harness.Infrastructure.Languages.Yaml;
using Harness.Languages;

namespace Harness.Host;

/// <summary>The checks this version of the harness ships, in execution order.</summary>
/// <remarks>
/// The frame is read first, because every frame question reads it. Then the analyses the
/// harness performs itself, which drift between repositories and which no repository's own
/// pipeline measures the same way. Then the frame's questions, the same for every repository.
/// </remarks>
internal static class CheckRegistry
{
    private static readonly CSharpSources CSharp = new();

    private static readonly YamlSources Yaml = new();

    private static readonly TypeScriptSources TypeScript = new();

    private static readonly GoSources Go = new();

    private static readonly AnsibleSources Ansible = new();

    public static readonly ICommitIntegration CommitIntegration = new CommitHookSetup();

    /// <summary>The analyzers whose graph is measured as a DSM; the Ansible role graph is judged for cycles only.</summary>
    public static readonly IReadOnlyList<ILanguageAnalyzer> LanguageAnalyzers =
        [new CSharpAnalyzer(CSharp), new TypeScriptAnalyzer(TypeScript), new GoAnalyzer(Go)];

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
            new CommentLineCheck(new CSharpCommentedSources(CSharp)),
            new CommentLineCheck(Yaml),
            new CommentLineCheck(TypeScript),
            new CommentLineCheck(new GoCommentedSources(Go)),
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

            .. FrameQuestions.All.Select(question => new FrameQuestionCheck(question)),
        ];
    }

}
