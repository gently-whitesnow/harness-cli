using System.Text.RegularExpressions;
using Harness.Languages;

namespace Harness.Infrastructure.Languages.TypeScript;

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
            var bindings = Bindings(declaration, reexport);
            var typeOnly = TypeKeyword().IsMatch(declaration) || bindings is [_, ..] && bindings.All(binding => binding.TypeOnly);
            result.Add(new TypeScriptImport(specifier, Line(code, match.Index), typeOnly, bindings, reexport, false));
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

    /// <summary>
    /// Every named specifier of the declaration, or null when it reaches the whole module (`import * as ns`,
    /// `export *`, side-effect imports) or when a specifier cannot be read literally.
    /// </summary>
    private static List<TypeScriptBinding>? Bindings(string head, bool reexport)
    {
        head = head.Trim();
        if (reexport)
        {
            if (NamespaceReexport().Match(head) is { Success: true } star)
            {
                return [new TypeScriptBinding(star.Groups["alias"].Value, null, null, star.Groups["type"].Success)];
            }

            if (head.Contains('*', StringComparison.Ordinal) || ListOf(head) is not { } exported)
            {
                return null;
            }

            return ReadList(exported)?.Select(item => new TypeScriptBinding(item.Alias, item.Name, null, item.TypeOnly)).ToList();
        }

        if (head.Contains('*', StringComparison.Ordinal))
        {
            return null;
        }

        var bindings = new List<TypeScriptBinding>();
        if (DefaultImport().Match(head) is { Success: true } importedDefault)
        {
            bindings.Add(new TypeScriptBinding("default", "default", importedDefault.Groups["local"].Value, false));
        }

        if (ListOf(head) is { } imported)
        {
            if (ReadList(imported) is not { } named)
            {
                return null;
            }

            bindings.AddRange(named.Select(item => new TypeScriptBinding(item.Name, item.Name, item.Alias, item.TypeOnly)));
        }

        return bindings.Count == 0 ? null : bindings;
    }

    /// <summary>Reads `A, B as C, type D,` from a masked brace list; null when any specifier is not a plain name.</summary>
    public static IReadOnlyList<(string Name, string Alias, bool TypeOnly)>? ReadList(string list)
    {
        var result = new List<(string, string, bool)>();
        foreach (var item in list.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0))
        {
            if (Specifier().Match(item) is not { Success: true } specifier)
            {
                return null;
            }

            var name = specifier.Groups["name"].Value;
            result.Add((name, specifier.Groups["alias"].Success ? specifier.Groups["alias"].Value : name, specifier.Groups["type"].Success));
        }

        return result;
    }

    private static string? ListOf(string head)
    {
        var open = head.IndexOf('{', StringComparison.Ordinal);
        var close = head.IndexOf('}', StringComparison.Ordinal);
        return open >= 0 && close > open ? head[(open + 1)..close] : null;
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

    [GeneratedRegex(@"^(?:import|export)\s+type\b(?!\s*(?:,|from\b))", RegexOptions.CultureInvariant)]
    private static partial Regex TypeKeyword();

    [GeneratedRegex(@"^import\s+(?:type\s+)?(?<local>[$\w]+)\s*(?:,|\bfrom\s*$)", RegexOptions.CultureInvariant)]
    private static partial Regex DefaultImport();

    [GeneratedRegex(@"^export\s+(?<type>type\s+)?\*\s*as\s+(?<alias>[$\w]+)\s+from\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceReexport();

    [GeneratedRegex(@"^(?:(?<type>type)\s+(?!as\b|$))?(?<name>[$\w]+)(?:\s+as\s+(?<alias>[$\w]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Specifier();
}
