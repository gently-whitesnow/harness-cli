namespace Harness.Checks.Frame;

/// <summary>
/// Tells a test project apart from a test file by the address alone. The harness reads C# and
/// TypeScript/JavaScript, so those are the source suffixes it recognises; the address is
/// never looked up in the repository.
/// </summary>
internal static class TestSuiteAddress
{
    private static readonly string[] SourceSuffixes =
        [".cs", ".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"];

    public static bool IsSourceFile(string path)
        => SourceSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The directories the named files live in, each once, as a JSON array to paste.</summary>
    public static string Owners(IEnumerable<string> files)
    {
        var owners = files
            .Select(file => file.Contains('/', StringComparison.Ordinal) ? file[..file.LastIndexOf('/')] : ".")
            .Distinct(StringComparer.Ordinal)
            .Select(owner => $"\"{owner}\"");
        return $"[{string.Join(", ", owners)}]";
    }
}
