using Harness.Checks;
using Harness.Checks.Architecture;
using Harness.Checks.Comments;
using Harness.Checks.Commits;
using Harness.Checks.Complexity;
using Harness.Checks.Dependencies;
using Harness.Checks.DotNet;
using Harness.Checks.Duplication;
using Harness.Checks.Frame;
using Harness.Checks.LintSuppressions;
using Harness.Checks.TypesPerFile;
using Harness.Git;
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

    public static readonly ICommitIntegration CommitIntegration = new CommitHookSetup();

    public static readonly IReadOnlyList<ILanguageAnalyzer> LanguageAnalyzers =
        [new CSharpAnalyzer(CSharp), new GoAnalyzer(Go)];

    public static readonly IReadOnlyList<IRepositoryCheck> All = Shipped();

    private static IReadOnlyList<IRepositoryCheck> Shipped()
    {
        var csharpAnalyzer = LanguageAnalyzers.Single(analyzer => analyzer.Language == Language.CSharp);

        return
        [
            new HarnessConfigCheck(),
            new HarnessCoverageCheck(),

            .. SlicedDotNetShapeCheck.Rules.Keys.Select(rule => new SlicedDotNetShapeCheck(csharpAnalyzer, rule)),
            .. LanguageAnalyzers.Select(analyzer => new ComplexityCheck(analyzer)),

            new DocumentationPolicyCheck(),
            new CommitSetupCheck(CommitIntegration),
            new CommentLineCheck(new CSharpCommentedSources(CSharp)),
            new CommentLineCheck(Yaml),
            new CommentLineCheck(TypeScript),
            new CommentLineCheck(new GoCommentedSources(Go)),
            new TypesPerFileCheck(CSharp),
            new DependenciesCheck(csharpAnalyzer),
            new DuplicationCheck(new CSharpNormalizedSources(CSharp)),
            new DuplicationCheck(new GoNormalizedSources(Go)),
            new LintSuppressionsCheck(Go),

            new BuildPropertiesCheck(),
            new CentralPackagesCheck(),
            new SolutionFormatCheck(),
            new EditorConfigCheck(),
            new WarningSuppressionsCheck(),

            .. FrameQuestions.All.Select(question => new FrameQuestionCheck(question)),
        ];
    }

}
