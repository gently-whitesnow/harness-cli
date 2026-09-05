namespace Harness.Checks.TypesPerFile;

internal static class TypesPerFileExplanation
{
    public const string Text =
        """
        Rationale
          An authored C# file should have one primary top-level class or record, so its name,
          review history and navigation point at one concept. Nested implementation details do
          not create a second file-level concept.

        What it reads
          Tracked authored `.cs` files. Generated files, generated content markers and build
          output locations are excluded by the shared C# source reader.

        What fails
          More than one top-level `class` or `record` declaration in the same file. Interfaces,
          structs, enums and nested types do not count toward the limit.

        Remediation
          Move each additional top-level class or record to its own authored `.cs` file.

        Applicability
          All C# checks can be disabled together when they do not apply:

          "applicability": {
            "csharp": { "applicable": false, "reason": "why C# checks do not apply" }
          }
        """;
}
