namespace Harness.Infrastructure.Languages.TypeScript;

internal static class TypeScriptFile
{
    private static readonly string[] Extensions = [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"];
    public static bool IsSource(string path)
        => Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static bool IsTest(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        return name.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".stories.", StringComparison.OrdinalIgnoreCase)
            || path.Split('/').Any(part => part is "__tests__" or "__mocks__" or "tests" or "e2e");
    }
}
