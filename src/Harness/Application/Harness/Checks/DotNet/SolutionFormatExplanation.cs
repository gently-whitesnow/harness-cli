namespace Harness.Checks.DotNet;

internal static class SolutionFormatExplanation
{
    public const string Text =
        """
        Rationale
          The XML .slnx format is compact, reviewable and the .NET 10 default. A solution should
          also remain a truthful build entry point as projects are added or removed.

        What it reads
          Tracked SDK-style projects, legacy .sln files and .slnx XML. No build is run. A
          solution written without `git add` is not in the index and therefore counts as absent;
          the run names such a file under `not in the index`.

        What fails
          Any tracked .sln; no .slnx when the repository has multiple projects; malformed .slnx;
          or a tracked SDK-style project absent from every tracked .slnx file.

        Remediation
          Run `dotnet sln <file>.sln migrate`, remove the legacy file, and add every authored
          project to an appropriate tracked .slnx solution.
        """;
}
