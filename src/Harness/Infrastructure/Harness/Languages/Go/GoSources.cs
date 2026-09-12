using System.Text.RegularExpressions;
using Harness.Languages;
using Harness.Languages.Go;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.Go;

/// <summary>
/// The tracked Go a repository ships, read once for the whole run. Files the go tool itself
/// ignores — vendor/, testdata/, directories starting with `_` or `.` — and files carrying the
/// canonical `Code generated ... DO NOT EDIT.` header are not authored prose and are skipped.
/// </summary>
internal sealed partial class GoSources : IGoSources
{
    private static readonly string[] Directives = ["go:", "line ", "export ", "extern ", "nolint", "lint:"];

    private static readonly string[] TopLevel = ["func ", "func(", "type ", "var ", "const ", "import ", "package "];

    private IRepository? read;
    private (IReadOnlyList<GoFile> Files, IReadOnlyList<string> MarkedGenerated, string? Failure) result;

    public (IReadOnlyList<GoFile> Files, string? Failure) Read(IRepository repository)
    {
        var (files, _, failure) = Discover(repository);
        return (files, failure);
    }

    public IReadOnlyList<string> MarkedGenerated(IRepository repository) => Discover(repository).MarkedGenerated;

    /// <summary>Whether the go tool would read this path: vendor, testdata and `_`/`.` directories are outside.</summary>
    public static bool IsAuthoredLocation(string path)
        => !RepositoryLocations.IsGenerated(path)
            && !path.Split('/').SkipLast(1).Any(segment => segment == "testdata" || segment.StartsWith('_') || segment.StartsWith('.'));

    private (IReadOnlyList<GoFile> Files, IReadOnlyList<string> MarkedGenerated, string? Failure) Discover(IRepository repository)
    {
        if (!ReferenceEquals(read, repository))
        {
            result = ReadAll(repository);
            read = repository;
        }

        return result;
    }

    private static (IReadOnlyList<GoFile> Files, IReadOnlyList<string> MarkedGenerated, string? Failure) ReadAll(
        IRepository repository)
    {
        var candidates = repository.TrackedEntries
            .Where(entry => !entry.IsSymbolicLink)
            .Where(entry => entry.Path.EndsWith(".go", StringComparison.Ordinal))
            .Where(entry => IsAuthoredLocation(entry.Path))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal);

        var files = new List<GoFile>();
        var marked = new List<string>();
        foreach (var entry in candidates)
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], [], failure ?? $"Could not read '{entry.Path}'.");
            }

            if (IsGeneratedContent(text))
            {
                marked.Add(entry.Path);
                continue;
            }

            files.Add(Parse(entry.Path, text));
        }

        return (files, marked, null);
    }

    // The go tool's own rule: the header is a line by itself before the package clause.
    private static bool IsGeneratedContent(string text)
        => text.Split('\n')
            .TakeWhile(line => !line.StartsWith("package ", StringComparison.Ordinal))
            .Any(line => GeneratedHeader().IsMatch(line.TrimEnd('\r')));

    private static GoFile Parse(string path, string text)
    {
        var (masked, regions) = GoMask.Apply(text);
        var lineStarts = LineStarts(text);
        var lines = lineStarts.Length;
        var hasCode = new bool[lines];
        var hasComment = new bool[lines];
        var comments = new List<GoComment>();

        for (var line = 0; line < lines; line++)
        {
            var start = lineStarts[line];
            var end = line + 1 < lines ? lineStarts[line + 1] : masked.Length;
            hasCode[line] = masked.AsSpan(start, end - start).ContainsAnyExcept(" \t\r\n");
        }

        foreach (var region in regions)
        {
            var first = LineIndexOf(lineStarts, region.Start);
            var last = LineIndexOf(lineStarts, Math.Max(region.Start, region.End - 1));
            if (region.Content == MaskedContent.Comment)
            {
                var body = text[region.Start..region.End];
                if (body.StartsWith("//", StringComparison.Ordinal))
                {
                    comments.Add(new GoComment(first + 1, body[2..].Trim()));
                }

                // A directive is an instruction to a tool, not prose about the code.
                var directive = body.StartsWith("//", StringComparison.Ordinal)
                    && Directives.Any(prefix => body.AsSpan(2).StartsWith(prefix, StringComparison.Ordinal));
                for (var line = first; line <= last; line++)
                {
                    if (directive)
                    {
                        hasCode[line] = true;
                    }
                    else
                    {
                        hasComment[line] = true;
                    }
                }
            }
            else
            {
                // A raw string spanning lines carries content on every one of them.
                for (var line = first; line <= last; line++)
                {
                    hasCode[line] = true;
                }
            }
        }

        var excluded = DocCommentLines(masked, lineStarts, hasCode, hasComment);
        var commentLines = 0;
        var authoredLines = 0;
        for (var line = 0; line < lines; line++)
        {
            if (excluded[line])
            {
                continue;
            }

            if (hasComment[line])
            {
                commentLines++;
            }

            if (hasCode[line] || hasComment[line])
            {
                authoredLines++;
            }
        }

        return new GoFile(
            path,
            PackageOf(masked),
            ImportsOf(text, masked, regions, lineStarts),
            masked,
            regions,
            commentLines,
            authoredLines,
            comments);
    }

    /// <summary>
    /// The doc comment of a top-level declaration: comment-only lines immediately above a line
    /// that starts, in column 0, with func, type, var, const, import or package. The language
    /// asks for these on exported identifiers, so they are the language's convention rather
    /// than the author's prose and leave both counts.
    /// </summary>
    private static bool[] DocCommentLines(string masked, int[] lineStarts, bool[] hasCode, bool[] hasComment)
    {
        var excluded = new bool[lineStarts.Length];
        for (var line = 0; line < lineStarts.Length; line++)
        {
            if (!StartsTopLevelDeclaration(masked, lineStarts, line))
            {
                continue;
            }

            for (var above = line - 1; above >= 0 && hasComment[above] && !hasCode[above]; above--)
            {
                excluded[above] = true;
            }
        }

        return excluded;
    }

    private static bool StartsTopLevelDeclaration(string masked, int[] lineStarts, int line)
    {
        var start = lineStarts[line];
        var end = line + 1 < lineStarts.Length ? lineStarts[line + 1] : masked.Length;
        var span = masked.AsSpan(start, end - start);
        foreach (var keyword in TopLevel)
        {
            if (span.StartsWith(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string PackageOf(string masked)
    {
        var match = PackageClause().Match(masked);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    // Import paths are string literals, so they are read from the original text at the
    // positions the mask recorded; the mask only says where an import clause is.
    private static List<GoImport> ImportsOf(string text, string masked, IReadOnlyList<MaskedRegion> regions, int[] lineStarts)
    {
        var imports = new List<GoImport>();
        var strings = regions.Where(region => region.Content == MaskedContent.StringLiteral).ToList();
        foreach (Match clause in ImportClause().Matches(masked))
        {
            var from = clause.Index + clause.Length;
            var to = clause.Groups[1].Success ? masked.IndexOf(')', from) : masked.IndexOf('\n', from);
            if (to < 0)
            {
                to = masked.Length;
            }

            foreach (var literal in strings.Where(region => region.Start >= from && region.End <= to))
            {
                var value = text[literal.Start..literal.End].Trim('"', '`');
                if (value.Length > 0)
                {
                    imports.Add(new GoImport(value, LineIndexOf(lineStarts, literal.Start) + 1));
                }
            }
        }

        return imports;
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n' && index + 1 < text.Length)
            {
                starts.Add(index + 1);
            }
        }

        return [.. starts];
    }

    private static int LineIndexOf(int[] lineStarts, int index)
    {
        var found = Array.BinarySearch(lineStarts, index);
        return found >= 0 ? found : ~found - 1;
    }

    [GeneratedRegex(@"^// Code generated .* DO NOT EDIT\.$")]
    private static partial Regex GeneratedHeader();

    [GeneratedRegex(@"^package[ \t]+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline)]
    private static partial Regex PackageClause();

    [GeneratedRegex(@"^import[ \t]*(\()?", RegexOptions.Multiline)]
    private static partial Regex ImportClause();
}
