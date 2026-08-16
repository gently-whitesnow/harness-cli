using Harness.Checks.Surfaces;
using Harness.Git;

namespace Harness.Checks.Declarations;

/// <summary>
/// Everything this version of the harness recognizes in Git as a sign that a repository
/// owns a piece of quality machinery. The lists live here because two readers need the same
/// answer: the check that looks for them, and `explain`, which has to state what was
/// actually looked for.
/// </summary>
/// <remarks>
/// These lists never decide that a repository has a capability — that is what the
/// declaration is for. They decide only when a declaration is refuted: a repository that
/// declares it has no unit tests while a tracked project declares a test framework has said
/// something Git contradicts. The asymmetry is deliberate. Failing to recognize evidence
/// costs nothing, because the declaration already carries the claim; recognizing it wrongly
/// would fail a repository over a list this file happens to be missing.
/// </remarks>
internal static class RecognizedEvidence
{
    public static readonly string[] UnitTestScripts = ["test:unit", "test:ci", "test:run", "test"];

    /// <summary>Libraries whose purpose is running the real thing rather than a stand-in.</summary>
    public static readonly string[] IntegrationPackages =
    [
        "Microsoft.AspNetCore.Mvc.Testing",
        "Microsoft.AspNetCore.TestHost",
        "Testcontainers",
        "DotNet.Testcontainers",
        "WireMock.Net",
        "Respawn",
    ];

    public static readonly string[] IntegrationScripts = ["test:integration", "test:e2e", "e2e"];

    public static readonly string[] IntegrationDependencies = ["@playwright/test", "cypress", "supertest"];

    /// <summary>Libraries built to assert structure rather than behaviour.</summary>
    public static readonly string[] ArchitecturePackages = ["NetArchTest", "ArchUnitNET"];

    public static readonly string[] ArchitectureDependencies = ["dependency-cruiser", "eslint-plugin-boundaries"];

    public static readonly string[] FormatFiles =
    [
        ".editorconfig",
        ".prettierrc",
        ".prettierrc.json",
        ".prettierrc.yaml",
        ".prettierrc.yml",
        "prettier.config.js",
        "prettier.config.mjs",
        ".clang-format",
    ];

    public static readonly string[] FormatScripts = ["format", "format:check", "fmt"];

    public static readonly string[] LintFiles =
    [
        ".eslintrc",
        ".eslintrc.json",
        ".eslintrc.cjs",
        "eslint.config.js",
        "eslint.config.mjs",
        "eslint.config.ts",
        ".globalconfig",
        "stylecop.json",
        "biome.json",
    ];

    public static readonly string[] LintScripts = ["lint", "lint:check"];

    public static readonly string[] BuildScripts = ["build", "compile"];

    public static readonly string[] TypecheckFiles = ["tsconfig.json"];

    public static readonly string[] TypecheckScripts = ["typecheck", "type-check", "tsc"];

    /// <summary>
    /// Tracked files whose name is one of the given ones, anywhere the repository's own code
    /// lives. Matching by name rather than by full path keeps a monorepo's per-package
    /// configuration visible without the harness having to know its layout.
    /// </summary>
    public static IReadOnlyList<string> TrackedNamed(GitRepository repository, IReadOnlyList<string> names)
        => repository.TrackedEntries
            .Where(entry => !RepositoryLocations.IsGenerated(entry.Path))
            .Where(entry => names.Contains(FileName(entry.Path), StringComparer.OrdinalIgnoreCase))
            .Select(entry => entry.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    /// <summary>Declared scripts of the web surface, named as the report names them.</summary>
    public static IEnumerable<EvidenceItem> Scripts(WebSurface web, IReadOnlyList<string> names)
        => web.Kind != WebSurfaceKind.Present
            ? []
            : web.ScriptsAmong(names)
                .Select(script => new EvidenceItem(web.ManifestPath, $"declares the script `{script}`"));

    /// <summary>Declared dependencies of the web surface, named as the report names them.</summary>
    public static IEnumerable<EvidenceItem> Dependencies(WebSurface web, IReadOnlyList<string> names)
        => web.Kind != WebSurfaceKind.Present
            ? []
            : web.DependenciesAmong(names)
                .Select(package => new EvidenceItem(web.ManifestPath, $"depends on {package}"));

    /// <summary>Tracked files whose presence is itself the sign, named as the report names them.</summary>
    public static IEnumerable<EvidenceItem> Files(GitRepository repository, IReadOnlyList<string> names)
        => TrackedNamed(repository, names).Select(path => new EvidenceItem(path, "is tracked"));

    /// <summary>The list as `explain` prints it, so prose and behaviour cannot drift apart.</summary>
    public static string List(IReadOnlyList<string> names) => string.Join(", ", names);

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];
}
