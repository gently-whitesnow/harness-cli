namespace Harness.Config;

/// <summary>
/// Matches a repository-relative path against a glob: `*` and `?` stay inside one segment,
/// `**` spans zero or more whole segments. The BCL matcher does not know `**`, and override
/// zones are directories, so segment-aware matching is the smallest thing that works.
/// </summary>
internal static class PathGlob
{
    public static bool Matches(string pattern, string path)
        => MatchesFrom(pattern.Split('/'), 0, path.Split('/'), 0);

    private static bool MatchesFrom(string[] pattern, int patternIndex, string[] path, int pathIndex)
    {
        while (patternIndex < pattern.Length)
        {
            if (pattern[patternIndex] == "**")
            {
                for (var anchor = pathIndex; anchor <= path.Length; anchor++)
                {
                    if (MatchesFrom(pattern, patternIndex + 1, path, anchor))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (pathIndex >= path.Length || !SegmentMatches(pattern[patternIndex], path[pathIndex]))
            {
                return false;
            }

            patternIndex++;
            pathIndex++;
        }

        return pathIndex == path.Length;
    }

    private static bool SegmentMatches(string pattern, string segment)
    {
        var patternIndex = 0;
        var segmentIndex = 0;
        var star = -1;
        var anchor = 0;

        while (segmentIndex < segment.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?' || pattern[patternIndex] == segment[segmentIndex]))
            {
                patternIndex++;
                segmentIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                star = patternIndex++;
                anchor = segmentIndex;
            }
            else if (star >= 0)
            {
                patternIndex = star + 1;
                segmentIndex = ++anchor;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
