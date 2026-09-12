using Harness.Languages;

namespace Harness.Infrastructure.Languages.Go;

internal static class GoBuildConstraints
{
    public static bool Ignores(string text)
    {
        var (masked, regions) = GoMask.Apply(text);
        var codeStart = 0;
        while (codeStart < masked.Length && char.IsWhiteSpace(masked[codeStart]))
        {
            codeStart++;
        }

        var modern = regions.Where(region => region.Start < codeStart && region.Content == MaskedContent.Comment)
            .Where(region => text.AsSpan(region.Start).StartsWith("//", StringComparison.Ordinal))
            .Where(region => text.AsSpan(text.LastIndexOf('\n', region.Start) + 1,
                region.Start - text.LastIndexOf('\n', region.Start) - 1).Trim().IsEmpty)
            .Select(region => text[region.Start..region.End].Trim())
            .Where(line => IsDirective(line, "//go:build"))
            .ToList();
        if (modern.Count > 0)
        {
            return modern.Count == 1 && OnlyIgnore(modern[0]["//go:build".Length..], legacy: false);
        }

        var legacy = new List<string>();
        var separated = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                separated = legacy.Count;
            }
            else if (!line.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }
            else if (IsDirective(line[2..].TrimStart(), "+build"))
            {
                legacy.Add(line[2..].TrimStart()["+build".Length..]);
            }
        }

        return legacy.Take(separated).Any(expression => OnlyIgnore(expression, legacy: true));
    }

    private static bool IsDirective(string line, string prefix)
        => line.StartsWith(prefix, StringComparison.Ordinal)
            && (line.Length == prefix.Length || char.IsWhiteSpace(line[prefix.Length]));

    private static bool OnlyIgnore(string expression, bool legacy)
    {
        var normalized = legacy ? expression : expression.Replace("&&", " ", StringComparison.Ordinal).Replace("||", " ", StringComparison.Ordinal);
        char[] separators = legacy ? [' ', '\t', ','] : [' ', '\t', '(', ')'];
        var tags = normalized.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return tags.Length > 0 && tags.All(tag => tag == "ignore");
    }
}
