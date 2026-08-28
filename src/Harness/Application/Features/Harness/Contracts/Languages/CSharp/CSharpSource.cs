namespace Harness.Languages.CSharp;

internal enum MaskedContent
{
    Comment,
    Preprocessor,
    StringLiteral,
    CharacterLiteral,
}

/// <summary>
/// One tracked C# file, reduced to code by <see cref="CSharpMask"/> and indexed by line, so
/// every measurement taken on it can be reported at the place it was taken.
/// </summary>
internal sealed class CSharpSource
{
    private readonly int[] lineStarts;
    private readonly bool[] isLogical;
    private readonly bool[] hasComment;

    private CSharpSource(
        string path,
        string masked,
        IReadOnlyList<MaskedRegion> maskedRegions,
        int[] lineStarts,
        bool[] isLogical,
        bool[] hasComment)
    {
        Path = path;
        Masked = masked;
        MaskedRegions = maskedRegions;
        this.lineStarts = lineStarts;
        this.isLogical = isLogical;
        this.hasComment = hasComment;
    }

    public string Path { get; }

    public string Masked { get; }

    public IReadOnlyList<MaskedRegion> MaskedRegions { get; }

    public int LineCount => lineStarts.Length;

    public int LogicalLines => LogicalLinesBetween(1, LineCount);

    public int CommentLines => hasComment.Count(line => line);

    public int AuthoredLines
        => Enumerable.Range(0, LineCount).Count(line => isLogical[line] || hasComment[line]);

    public static CSharpSource Create(
        string path,
        string masked,
        List<MaskedRegion> regions)
    {
        var lineStarts = LineStarts(masked);
        return new CSharpSource(
            path,
            masked,
            regions,
            lineStarts,
            ClassifyLines(masked, lineStarts, regions),
            ClassifyCommentLines(lineStarts, regions));
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

    private static int LineIndexOf(int[] lineStarts, int index)
    {
        var found = Array.BinarySearch(lineStarts, index);
        return found >= 0 ? found : ~found - 1;
    }
}
