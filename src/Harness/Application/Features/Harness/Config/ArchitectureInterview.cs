namespace Harness.Config;

internal enum RepositoryKind
{
    Application,
    StandaloneLibrary,
}

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
            _ => (null, "Initialization requires one answer: 'application' or 'library'."),
        };
    }
}
