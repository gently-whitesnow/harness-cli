using System.Text.RegularExpressions;
using Harness.Languages;

namespace Harness.Infrastructure.Languages.TypeScript;

internal sealed record TypeScriptImport(string Specifier, int Line, bool TypeOnly, string? ImportedName, bool Reexport, bool Dynamic,
    string? SourceName = null, string? LocalName = null);

/// <summary>Reads literal module specifiers; comments and string contents cannot introduce syntax.</summary>
internal static partial class TypeScriptImports
{
    public static IReadOnlyList<TypeScriptImport> Read(string text)
    {
        var (masked, regions) = TypeScriptMask.Apply(text);
        var chars = masked.ToCharArray();
        foreach (var region in regions.Where(region => region.Content == MaskedContent.StringLiteral))
        {
            for (var i = region.Start; i < region.End; i++)
            {
                chars[i] = text[i];
            }
        }

        var code = new string(chars);
        var result = new List<TypeScriptImport>();
        foreach (Match match in StaticImport().Matches(code))
        {
            if (InsideLiteral(match.Index, regions))
            {
                continue;
            }

            var quote = match.Groups["quote"].Value;
            var specifier = match.Groups["path"].Value;
            if (quote == "`" && specifier.Contains("${", StringComparison.Ordinal))
            {
                result.Add(new TypeScriptImport("<dynamic>", Line(code, match.Index), false, null, false, true));
                continue;
            }

            var declaration = match.Groups["head"].Value;
            var reexport = declaration.TrimStart().StartsWith("export", StringComparison.Ordinal);
            var (name, sourceName, localName) = ImportedName(declaration, reexport);
            result.Add(new TypeScriptImport(specifier, Line(code, match.Index),
                Regex.IsMatch(declaration, @"\btype\b", RegexOptions.CultureInvariant), name,
                reexport, false, sourceName, localName));
        }
        foreach (Match match in DynamicImport().Matches(code))
        {
            if (InsideLiteral(match.Index, regions))
            {
                continue;
            }

            var specifier = match.Groups["path"].Value;
            if (match.Groups["quote"].Value == "`" && specifier.Contains("${", StringComparison.Ordinal))
            {
                result.Add(new TypeScriptImport("<dynamic>", Line(code, match.Index), false, null, false, true));
                continue;
            }

            result.Add(new TypeScriptImport(specifier, Line(code, match.Index), false, null, false, true));
        }
        foreach (Match match in NonLiteralImport().Matches(code))
        {
            if (InsideLiteral(match.Index, regions))
            {
                continue;
            }

            var argument = match.Groups["argument"].Value.TrimStart();
            if (argument.Length > 0 && argument[0] is not ('\'' or '"' or '`'))
            {
                result.Add(new TypeScriptImport("<dynamic>", Line(code, match.Index), false, null, false, true));
            }
        }

        return result.OrderBy(import => import.Line).ToList();
    }

    private static (string? Exposed, string? Source, string? Local) ImportedName(string head, bool reexport)
    {
        if (head.Contains('*'))
        {
            return (null, null, null);
        }

        var match = Regex.Match(head, @"\{\s*(?:type\s+)?(?<name>[$\w]+)(?:\s+as\s+(?<alias>[$\w]+))?", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            if (head[match.Index..].Contains(',', StringComparison.Ordinal))
            {
                return (null, null, null);
            }

            var source = match.Groups["name"].Value;
            var alias = match.Groups["alias"].Success ? match.Groups["alias"].Value : source;
            return reexport ? (alias, source, null) : (source, source, alias);
        }

        if (!reexport && Regex.Match(head, @"^import\s+(?:type\s+)?(?<local>[$\w]+)\s+from\s*$", RegexOptions.CultureInvariant) is { Success: true } importedDefault)
        {
            return ("default", "default", importedDefault.Groups["local"].Value);
        }

        return (null, null, null);
    }

    private static bool InsideLiteral(int offset, IReadOnlyList<MaskedRegion> regions)
        => regions.Any(region => region.Content == MaskedContent.StringLiteral && region.Start <= offset && offset < region.End);

    private static int Line(string text, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    [GeneratedRegex("""\b(?<head>(?:import|export)\s+(?:(?:\{[^}]*\})|[^;'"`\n])*?\bfrom\s*|import\s*)(?<quote>['"`])(?<path>[^'"`\r\n]*)(?:['"`])""", RegexOptions.CultureInvariant)]
    private static partial Regex StaticImport();

    [GeneratedRegex("""\b(?:require|import)\s*\(\s*(?<quote>['"`])(?<path>[^'"`\r\n]*)(?:['"`])\s*\)""", RegexOptions.CultureInvariant)]
    private static partial Regex DynamicImport();

    [GeneratedRegex("""\bimport\s*\(\s*(?<argument>[^)]*)\)""", RegexOptions.CultureInvariant)]
    private static partial Regex NonLiteralImport();
}
