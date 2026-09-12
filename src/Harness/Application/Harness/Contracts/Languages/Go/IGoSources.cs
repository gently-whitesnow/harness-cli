using Harness.Repository;

namespace Harness.Languages.Go;

internal interface IGoSources
{
    const string NothingToAnalyze =
        "no tracked Go source outside generated, vendored, testdata and build-output locations";

    (IReadOnlyList<GoFile> Files, string? Failure) Read(IRepository repository);

    /// <summary>Tracked Go files in authored locations that a `Code generated ... DO NOT EDIT.` header excluded.</summary>
    IReadOnlyList<string> MarkedGenerated(IRepository repository);

    /// <summary>
    /// Tracked Go files in authored locations that a `//go:build ignore` (or legacy `// +build ignore`)
    /// constraint excluded: the go tool never builds them, so they are not the product's source.
    /// </summary>
    IReadOnlyList<string> MarkedIgnored(IRepository repository);
}
