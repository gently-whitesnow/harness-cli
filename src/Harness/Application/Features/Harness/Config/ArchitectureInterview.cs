using Harness.Contracts;

namespace Harness.Config;

internal static class ArchitectureInterview
{
    public static (RepositoryKind? Kind, string? Failure) Ask(TextReader input, TextWriter output)
    {
        output.Write("Repository kind [application/library]: ");
        var answer = input.ReadLine()?.Trim().ToLowerInvariant();
        return answer switch
        {
            "application" or "app" => (RepositoryKind.Application, null),
            "library" or "standalone-library" => (RepositoryKind.StandaloneLibrary, null),
            _ => (null, "Initialization requires one answer: 'application' or 'library'; "
                + "for non-interactive use, pass `--kind application` or `--kind library`."),
        };
    }
}
