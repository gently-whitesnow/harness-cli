using Harness.Checks.CSharp;
using Harness.Git;

namespace Harness.Checks.Duplication;

/// <summary>
/// Finds C# that repeats across files after normalization, and reports each repetition once
/// with every place it occurs. The evidence is lexical: it proves that two regions read the
/// same once names and literals are removed, which is a reason to look and never a proof
/// that the two behave alike. Nothing here is blocking.
/// </summary>
internal sealed class DuplicationCheck : IRepositoryCheck
{
    /// <summary>
    /// The comparison window, in normalized lines. Short enough to catch a repeated body,
    /// long enough that ordinary C# punctuation cannot fill it on its own.
    /// </summary>
    private const int WindowLines = 8;

    /// <summary>
    /// The tokens a window must carry — an average of three per line. It is what separates a
    /// repeated block from a run of braces, `return`s and closing parentheses, which every
    /// file has and which nobody can extract.
    /// </summary>
    private const int MinimumWindowTokens = 3 * WindowLines;

    /// <summary>Enough of the largest repetitions to act on; the rest are counted, not listed.</summary>
    private const int ShownBlocks = 5;

    /// <summary>Enough locations to see the shape of a repetition without printing an inventory.</summary>
    private const int ShownLocations = 4;

    public string Id => "duplication.csharp";

    public string Group => "duplication";

    public string Summary => "C# cross-file lexical repetition";

    public string Explanation => DuplicationExplanation.Text;

    public CheckEvaluation Evaluate(GitRepository repository)
    {
        var (sources, failure) = CSharpSources.Discover(repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (sources.Count == 0)
        {
            return CheckEvaluation.NotApplicable(CSharpSources.NothingToAnalyze);
        }

        return CheckEvaluation.From(Report(Repetitions(NormalizedFile.From(sources))));
    }

    /// <summary>
    /// Every maximal repetition, largest first. Windows are the unit of comparison but never
    /// the unit of reporting: a repetition is grown to its full extent in every file at once,
    /// and the windows it covers are then spent, so one clone cannot be reported as the
    /// dozens of overlapping windows it happens to contain.
    /// </summary>
    private static List<Repetition> Repetitions(IReadOnlyList<NormalizedFile> files)
    {
        var byShape = new Dictionary<long, List<Occurrence>>();
        foreach (var file in files)
        {
            for (var start = 0; start < file.Windows; start++)
            {
                if (file.IsComparable(start))
                {
                    var shape = file.ShapeOf(start);
                    if (!byShape.TryGetValue(shape, out var occurrences))
                    {
                        byShape[shape] = occurrences = [];
                    }

                    occurrences.Add(new Occurrence(file, start));
                }
            }
        }

        var repetitions = new List<Repetition>();
        foreach (var file in files)
        {
            for (var start = 0; start < file.Windows; start++)
            {
                if (file.IsSpent(start) || !byShape.TryGetValue(file.ShapeOf(start), out var candidates))
                {
                    continue;
                }

                // A shape bucket may hold windows that only hash alike, and windows already
                // spent by a larger repetition. What survives both is the actual group.
                var group = candidates
                    .Where(occurrence => !occurrence.File.IsSpent(occurrence.Start))
                    .Where(occurrence => occurrence.File.Matches(occurrence.Start, file, start))
                    .ToList();

                if (group.Count < 2 || group.Select(occurrence => occurrence.File.Path).Distinct().Count() < 2)
                {
                    continue;
                }

                repetitions.Add(Grow(group));
            }
        }

        return repetitions
            .OrderByDescending(repetition => repetition.Lines)
            .ThenBy(repetition => repetition.Regions[0].Path, StringComparer.Ordinal)
            .ThenBy(repetition => repetition.Regions[0].FirstLine)
            .ToList();
    }

    /// <summary>
    /// Extends one group of matching windows in both directions for as long as every
    /// occurrence still agrees, so the reported block is the whole repeated region rather
    /// than the arbitrary window that happened to reveal it.
    /// </summary>
    private static Repetition Grow(List<Occurrence> group)
    {
        // Growth stops at a window some earlier repetition already reported. Without that,
        // a group whose occurrences reach further than an earlier one would grow back over
        // lines that have been reported already and say the same thing a second time.
        var offset = 0;
        while (Agree(group, occurrence => occurrence.File.LineBefore(occurrence.Start + offset)))
        {
            offset--;
        }

        var length = WindowLines - offset;
        while (Agree(group, occurrence => occurrence.File.LineAt(occurrence.Start + offset + length)))
        {
            length++;
        }

        foreach (var occurrence in group)
        {
            occurrence.File.Spend(occurrence.Start + offset, length);
        }

        return new Repetition(
            length,
            group
                .Select(occurrence => occurrence.File.RegionAt(occurrence.Start + offset, length))
                .OrderBy(region => region.Path, StringComparer.Ordinal)
                .ThenBy(region => region.FirstLine)
                .ToList());
    }

    /// <summary>Whether every occurrence offers the same next line; a missing one never agrees.</summary>
    private static bool Agree(List<Occurrence> group, Func<Occurrence, int?> next)
    {
        var expected = next(group[0]);
        return expected is not null && group.All(occurrence => next(occurrence) == expected);
    }

    private static IReadOnlyList<Finding> Report(List<Repetition> repetitions)
    {
        var findings = new List<Finding>();

        foreach (var repetition in repetitions.Take(ShownBlocks))
        {
            findings.Add(new Finding(
                FindingSeverity.Advisory,
                repetition.Regions[0].ToString(),
                $"a lexically repeated block of {repetition.Lines} normalized lines occurs "
                    + $"{repetition.Regions.Count} times: {Listed(repetition.Regions)}"));
        }

        if (repetitions.Count > ShownBlocks)
        {
            findings.Add(new Finding(
                FindingSeverity.Advisory,
                repetitions[ShownBlocks].Regions[0].ToString(),
                $"{repetitions.Count} repeated blocks were found; the {ShownBlocks} largest are listed above"));
        }

        return findings;
    }

    private static string Listed(IReadOnlyList<Region> regions)
    {
        var shown = string.Join(", ", regions.Take(ShownLocations));
        var remaining = regions.Count - Math.Min(regions.Count, ShownLocations);
        return remaining > 0 ? $"{shown} and {remaining} more" : shown;
    }

    /// <summary>
    /// Where one occurrence is, as physical lines. The span is what makes the evidence
    /// readable: normalized lines are not physical lines, so a start alone would leave a
    /// reader unable to see how much of the file the repetition actually covers.
    /// </summary>
    private sealed record Region(string Path, int FirstLine, int LastLine)
    {
        public override string ToString()
            => LastLine > FirstLine ? $"{Path}:{FirstLine}-{LastLine}" : $"{Path}:{FirstLine}";
    }

    /// <param name="Lines">Normalized lines the repeated block spans.</param>
    /// <param name="Regions">Where it occurs, in path and then line order.</param>
    private sealed record Repetition(int Lines, IReadOnlyList<Region> Regions);

    /// <param name="Start">Index into the file's normalized lines.</param>
    private readonly record struct Occurrence(NormalizedFile File, int Start);

    /// <summary>
    /// One file as the comparison sees it: its normalized lines as identifiers, so comparing
    /// two regions is comparing integers rather than re-reading text.
    /// </summary>
    private sealed class NormalizedFile
    {
        private readonly IReadOnlyList<NormalizedLine> lines;

        /// <summary>One identifier per normalized line; equal identifiers are equal lines.</summary>
        private readonly int[] ids;

        private readonly int[] tokensUpTo;

        /// <summary>Windows already covered by a reported repetition.</summary>
        private readonly bool[] spent;

        private NormalizedFile(string path, IReadOnlyList<NormalizedLine> lines, int[] ids)
        {
            Path = path;
            this.ids = ids;
            this.lines = lines;

            Windows = Math.Max(0, ids.Length - WindowLines + 1);
            spent = new bool[Math.Max(Windows, 1)];

            tokensUpTo = new int[lines.Count + 1];
            for (var line = 0; line < lines.Count; line++)
            {
                tokensUpTo[line + 1] = tokensUpTo[line] + lines[line].TokenCount;
            }
        }

        public string Path { get; }

        /// <summary>How many comparison windows the file offers.</summary>
        public int Windows { get; }

        public static IReadOnlyList<NormalizedFile> From(IReadOnlyList<CSharpSource> sources)
        {
            var identifiers = new Dictionary<string, int>(StringComparer.Ordinal);
            var files = new List<NormalizedFile>();

            foreach (var source in sources)
            {
                var lines = CSharpNormalizer.Read(source);
                var ids = new int[lines.Count];
                for (var line = 0; line < lines.Count; line++)
                {
                    if (!identifiers.TryGetValue(lines[line].Tokens, out var id))
                    {
                        identifiers[lines[line].Tokens] = id = identifiers.Count;
                    }

                    ids[line] = id;
                }

                files.Add(new NormalizedFile(source.Path, lines, ids));
            }

            return files;
        }

        /// <summary>
        /// Whether the window opening at <paramref name="start"/> carries enough tokens to
        /// be worth comparing at all.
        /// </summary>
        public bool IsComparable(int start)
            => tokensUpTo[start + WindowLines] - tokensUpTo[start] >= MinimumWindowTokens;

        public bool IsSpent(int start) => spent[start];

        /// <summary>
        /// Records that a reported repetition covers <paramref name="length"/> lines from
        /// here. Every window that begins inside the region is spent, not only the ones that
        /// fit inside it, so no later repetition can begin on a line already reported.
        /// </summary>
        public void Spend(int start, int length)
        {
            for (var window = start; window < Math.Min(start + length, Windows); window++)
            {
                spent[window] = true;
            }
        }

        /// <summary>
        /// The line at <paramref name="index"/>, offered for growth forwards. A line already
        /// covered by a reported repetition is not offered: the same lines are never
        /// reported twice, whichever repetition reached them first.
        /// </summary>
        public int? LineAt(int index)
            => index >= 0 && index < ids.Length && !IsReported(index - WindowLines + 1)
                ? ids[index]
                : null;

        /// <summary>The line before <paramref name="index"/>, offered for growth backwards.</summary>
        public int? LineBefore(int index)
            => index > 0 && !IsReported(index - 1) ? ids[index - 1] : null;

        private bool IsReported(int window) => window >= 0 && window < Windows && spent[window];

        /// <summary>
        /// A hash of the window opening at <paramref name="start"/>. Buckets by shape keep
        /// the search linear; equality is always confirmed against the identifiers
        /// themselves, so a collision costs a comparison and never invents a repetition.
        /// </summary>
        public long ShapeOf(int start)
        {
            var shape = unchecked((long)14695981039346656037UL);
            for (var line = start; line < start + WindowLines; line++)
            {
                unchecked
                {
                    shape = (shape ^ ids[line]) * 1099511628211L;
                }
            }

            return shape;
        }

        public bool Matches(int start, NormalizedFile other, int otherStart)
        {
            for (var line = 0; line < WindowLines; line++)
            {
                if (ids[start + line] != other.ids[otherStart + line])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The physical lines a repeated block covers here.</summary>
        public Region RegionAt(int start, int length)
            => new(Path, lines[start].Line, lines[start + length - 1].Line);
    }
}
