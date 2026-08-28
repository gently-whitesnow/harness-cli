using Harness.Checks;
using Harness.Checks.Architecture;
using Harness.Checks.Comments;
using Harness.Checks.Commits;
using Harness.Checks.Complexity;
using Harness.Checks.Dependencies;
using Harness.Checks.DotNet;
using Harness.Checks.Duplication;
using Harness.Checks.Frame;
using Harness.Checks.TypesPerFile;
using Harness.Git;
using Harness.Infrastructure.Languages.CSharp;
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

    public static readonly ICommitIntegration CommitIntegration = new CommitHookSetup();

    public static readonly IReadOnlyList<ILanguageAnalyzer> LanguageAnalyzers =
        [new CSharpAnalyzer(CSharp)];

    public static readonly IReadOnlyList<IRepositoryCheck> All = Shipped();

    private static IReadOnlyList<IRepositoryCheck> Shipped()
    {
        var csharpAnalyzer = LanguageAnalyzers.Single(analyzer => analyzer.Language == Language.CSharp);

        return
        [
            new HarnessConfigCheck(),

            new SlicedDotNetShapeCheck(csharpAnalyzer),
            .. LanguageAnalyzers.Select(analyzer => new ComplexityCheck(analyzer, LanguageAnalyzers)),

            new DocumentationPolicyCheck(),
            new CommitSetupCheck(CommitIntegration),
            new CommentLineCheck(CSharp),
            new TypesPerFileCheck(CSharp),
            new DependenciesCheck(csharpAnalyzer),
            new DuplicationCheck(CSharp),

            new BuildPropertiesCheck(),
            new CentralPackagesCheck(),
            new SolutionFormatCheck(),

            new UnitTestFrameCheck(),
            new IntegrationTestFrameCheck(),
            new ArchitectureFrameCheck(),
            new FormatFrameCheck(),
            new LintFrameCheck(),
            new BuildFrameCheck(),
            new TypecheckFrameCheck(),
        ];
    }

}
