namespace Harness.Languages.CSharp;

/// <summary>
/// One tracked C# file, reduced to code by the infrastructure reader and indexed by line, so
/// every measurement taken on it can be reported at the place it was taken.
/// </summary>
internal sealed class CSharpSource
{
    private readonly int[] lineStarts;
    private readonly bool[] isLogical;
    private readonly bool[] hasComment;
    private readonly bool[] documents;

    private CSharpSource(
        string path,
        string masked,
        IReadOnlyList<MaskedRegion> maskedRegions,
        int[] lineStarts,
        bool[] isLogical,
        bool[] hasComment,
        bool[] documents)
    {
        Path = path;
        Masked = masked;
        MaskedRegions = maskedRegions;
        this.lineStarts = lineStarts;
        this.isLogical = isLogical;
        this.hasComment = hasComment;
        this.documents = documents;
    }

    public string Path { get; }

    public string Masked { get; }

    public IReadOnlyList<MaskedRegion> MaskedRegions { get; }

    public int LineCount => lineStarts.Length;

    public int LogicalLines => LogicalLinesBetween(1, LineCount);

    public int CommentLines => Enumerable.Range(0, LineCount).Count(line => hasComment[line] && !documents[line]);

    public int AuthoredLines
        => Enumerable.Range(0, LineCount).Count(line => !documents[line] && (isLogical[line] || hasComment[line]));

    public static CSharpSource Create(
        string path,
        string masked,
        List<MaskedRegion> regions,
        IReadOnlyCollection<MaskedRegion> documentation)
    {
        var lineStarts = LineStarts(masked);
        var isLogical = ClassifyLines(masked, lineStarts, regions);
        return new CSharpSource(
            path,
            masked,
            regions,
            lineStarts,
            isLogical,
            ClassifyCommentLines(lineStarts, regions),
            DocumentationLines(lineStarts, isLogical, documentation));
    }

    public int LineOf(int index)
    {
        var found = Array.BinarySearch(lineStarts, index);
        return found >= 0 ? found + 1 : ~found;
    }

    public int LogicalLinesBetween(int firstLine, int lastLine)
    {
        var first = Math.Max(1, firstLine);
        var last = Math.Min(LineCount, lastLine);

        var count = 0;
        for (var line = first; line <= last; line++)
        {
            if (isLogical[line - 1])
            {
                count++;
            }
        }

        return count;
    }

    public ReadOnlySpan<char> TextBetween(int firstLine, int lastLine)
    {
        if (firstLine > LineCount || lastLine < firstLine)
        {
            return ReadOnlySpan<char>.Empty;
        }

        var start = lineStarts[Math.Max(1, firstLine) - 1];
        var last = Math.Min(LineCount, lastLine);
        var end = last < LineCount ? lineStarts[last] : Masked.Length;
        return Masked.AsSpan(start, end - start);
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

    // Multi-line literal continuations carry file content even though masking removes it.
    private static bool[] ClassifyLines(string masked, int[] lineStarts, List<MaskedRegion> regions)
    {
        var isLogical = new bool[lineStarts.Length];
        for (var line = 0; line < lineStarts.Length; line++)
        {
            var start = lineStarts[line];
            var end = line + 1 < lineStarts.Length ? lineStarts[line + 1] : masked.Length;
            isLogical[line] = masked.AsSpan(start, end - start).ContainsAnyExcept(" \t\r\n");
        }

        foreach (var (start, end, _) in regions.Where(region => region.Content == MaskedContent.StringLiteral))
        {
            for (var index = start; index < end; index++)
            {
                if (masked[index] == '\n' && index + 1 < masked.Length)
                {
                    isLogical[LineIndexOf(lineStarts, index + 1)] = true;
                }
            }
        }

        return isLogical;
    }

    private static bool[] ClassifyCommentLines(int[] lineStarts, List<MaskedRegion> regions)
    {
        var hasComment = new bool[lineStarts.Length];
        foreach (var region in regions.Where(region => region.Content == MaskedContent.Comment))
        {
            var first = LineIndexOf(lineStarts, region.Start);
            var last = LineIndexOf(lineStarts, Math.Max(region.Start, region.End - 1));
            for (var line = first; line <= last; line++)
            {
                hasComment[line] = true;
            }
        }

        return hasComment;
    }

    // The XML documentation of a declaration: `///` lines directly above a line of code, as the
    // compiler attaches them. The language and CS1591 ask for these on public members, so they
    // are the language's convention rather than the author's prose and leave both counts.
    private static bool[] DocumentationLines(
        int[] lineStarts, bool[] isLogical, IReadOnlyCollection<MaskedRegion> documentation)
    {
        var isDocumentation = new bool[lineStarts.Length];
        foreach (var region in documentation)
        {
            var line = LineIndexOf(lineStarts, region.Start);
            isDocumentation[line] = !isLogical[line];
        }

        var documents = new bool[lineStarts.Length];
        for (var line = 1; line < lineStarts.Length; line++)
        {
            if (!isLogical[line])
            {
                continue;
            }

            for (var above = line - 1; above >= 0 && isDocumentation[above]; above--)
            {
                documents[above] = true;
            }
        }

        return documents;
    }

    private static int LineIndexOf(int[] lineStarts, int index)
    {
        var found = Array.BinarySearch(lineStarts, index);
        return found >= 0 ? found : ~found - 1;
    }
}
