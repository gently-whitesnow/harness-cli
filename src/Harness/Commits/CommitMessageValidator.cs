using Harness.Config;

namespace Harness.Commits;

/// <summary>Validates the human and machine-readable parts of one Git commit message.</summary>
internal static class CommitMessageValidator
{
    private const int RecommendedSubjectLength = 50;
    private const int MaximumSubjectLength = 72;
    private const int RecommendedBodyLineLength = 72;

    private static readonly string[] Types =
    [
        "feat", "fix", "refactor", "perf", "test", "docs", "style", "build", "ci", "chore", "revert",
    ];

    public static CommitMessageReport Validate(
        string message,
        CommitSettings settings,
        bool allowFixup)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var lines = Normalize(message).Split('\n');
        var subject = lines.Length == 0 ? "" : lines[0];

        if (string.IsNullOrWhiteSpace(subject))
        {
            return new CommitMessageReport(["line 1: subject must not be empty"], []);
        }

        if (IsGeneratedMessage(subject) || (allowFixup && IsAutosquashMessage(subject)))
        {
            return new CommitMessageReport([], []);
        }

        if (IsAutosquashMessage(subject))
        {
            errors.Add("line 1: fixup!/squash! commits must be resolved before this range is accepted");
            return new CommitMessageReport(errors, warnings);
        }

        ValidateSubject(subject, settings, errors, warnings);
        ValidateBody(lines, settings, errors, warnings);
        return new CommitMessageReport(errors, warnings);
    }

    private static void ValidateSubject(
        string subject,
        CommitSettings settings,
        List<string> errors,
        List<string> warnings)
    {
        if (subject.Length > MaximumSubjectLength)
        {
            errors.Add($"line 1: subject has {subject.Length} characters; maximum is {MaximumSubjectLength}");
        }
        else if (subject.Length > RecommendedSubjectLength)
        {
            warnings.Add(
                $"line 1: subject has {subject.Length} characters; {RecommendedSubjectLength} or fewer is easier to scan");
        }

        if (subject.EndsWith('.'))
        {
            errors.Add("line 1: subject must not end with a period");
        }

        var separator = subject.IndexOf(": ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            errors.Add("line 1: expected '<type>(<scope>): <description>'");
            return;
        }

        var prefix = subject[..separator];
        var description = subject[(separator + 2)..];
        if (prefix.EndsWith('!'))
        {
            prefix = prefix[..^1];
        }

        var openScope = prefix.IndexOf('(');
        var type = openScope < 0 ? prefix : prefix[..openScope];
        if (!Types.Contains(type, StringComparer.Ordinal))
        {
            errors.Add($"line 1: type '{type}' is not one of {string.Join(", ", Types)}");
        }

        if (openScope >= 0)
        {
            if (!prefix.EndsWith(')') || prefix.IndexOf(')', openScope) != prefix.Length - 1)
            {
                errors.Add("line 1: scope must be enclosed by one pair of parentheses");
            }
            else
            {
                ValidateScope(prefix[(openScope + 1)..^1], errors);
            }
        }

        if (description.Length == 0)
        {
            errors.Add("line 1: description must not be empty");
        }
        else
        {
            ValidateLanguage(description, settings.Language, errors);
        }
    }

    private static void ValidateScope(string scope, List<string> errors)
    {
        var valid = scope.Length > 0
            && scope[0] != '-'
            && scope[^1] != '-'
            && !scope.Contains("--", StringComparison.Ordinal)
            && scope.All(character => character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9'
                || character == '-');
        if (!valid)
        {
            errors.Add("line 1: scope must be lowercase kebab-case");
        }
    }

    private static void ValidateLanguage(
        string description,
        CommitLanguage language,
        List<string> errors)
    {
        var hasCyrillic = description.Any(IsCyrillic);
        var hasLatin = description.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        if (language == CommitLanguage.Russian && !hasCyrillic)
        {
            errors.Add("line 1: Russian commit descriptions must contain Cyrillic text");
        }

        if (language == CommitLanguage.English && (!hasLatin || hasCyrillic))
        {
            errors.Add("line 1: English commit descriptions must use English text");
        }
    }

    private static void ValidateBody(
        string[] lines,
        CommitSettings settings,
        List<string> errors,
        List<string> warnings)
    {
        var last = lines.Length - 1;
        while (last > 0 && lines[last].Length == 0)
        {
            last--;
        }

        if (last == 0)
        {
            return;
        }

        if (lines.Length < 2 || lines[1].Length != 0)
        {
            errors.Add("line 2: subject and body must be separated by a blank line");
            return;
        }

        for (var index = 2; index <= last; index++)
        {
            if (lines[index].Length > RecommendedBodyLineLength && !ContainsUrl(lines[index]))
            {
                warnings.Add(
                    $"line {index + 1}: body line has {lines[index].Length} characters; wrap near "
                    + RecommendedBodyLineLength);
            }
        }

        var content = lines.Skip(2).Take(last - 1).Where(line => line.Length > 0).ToList();
        if (content.Count > 0 && content.All(IsTrailer))
        {
            return;
        }

        RequirePopulatedSection(lines, settings.ContextHeading, settings, errors);
        RequirePopulatedSection(lines, settings.DecisionHeading, settings, errors);
    }

    private static void RequirePopulatedSection(
        string[] lines,
        string heading,
        CommitSettings settings,
        List<string> errors)
    {
        var index = lines.IndexOf(heading);
        if (index < 0)
        {
            errors.Add($"body: required section '{heading}' is missing");
            return;
        }

        var knownHeadings = new[]
        {
            settings.ContextHeading,
            settings.DecisionHeading,
            settings.BoundariesHeading,
            settings.ConsequencesHeading,
        };
        var populated = lines.Skip(index + 1)
            .TakeWhile(line => !knownHeadings.Contains(line, StringComparer.Ordinal) && !IsTrailer(line))
            .Any(line => !string.IsNullOrWhiteSpace(line));
        if (!populated)
        {
            errors.Add($"body: section '{heading}' must contain an explanation");
        }
    }

    private static bool IsTrailer(string line)
    {
        var separator = line.IndexOf(": ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        var key = line[..separator];
        return key == "BREAKING CHANGE"
            || key.All(character => character is >= 'A' and <= 'Z'
                || character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9'
                || character == '-');
    }

    private static bool IsGeneratedMessage(string subject)
        => subject.StartsWith("Merge ", StringComparison.Ordinal)
            || subject.StartsWith("Revert \"", StringComparison.Ordinal);

    private static bool IsAutosquashMessage(string subject)
        => subject.StartsWith("fixup! ", StringComparison.Ordinal)
            || subject.StartsWith("squash! ", StringComparison.Ordinal);

    private static bool IsCyrillic(char character)
        => character is >= '\u0400' and <= '\u04ff';

    private static bool ContainsUrl(string line)
        => line.Contains("https://", StringComparison.Ordinal)
            || line.Contains("http://", StringComparison.Ordinal);

    private static string Normalize(string message)
        => message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');

    private static int IndexOf(this string[] lines, string value)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (string.Equals(lines[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
