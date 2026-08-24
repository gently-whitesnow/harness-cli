using Harness.Structure;

namespace Harness.Languages.CSharp;

/// <summary>
/// Finds every place a file names a type the repository declares, and grades each place by
/// what the language allows to be written there. A declaration position — a construction, a
/// generic argument, an attribute, `typeof`, `catch`, or a type followed by the name it
/// introduces — can only be a type. Everything else is graded as a guess.
/// </summary>
internal sealed class CSharpReferenceScanner
{
    private static readonly HashSet<string> DeclarativeKeywords = new(StringComparer.Ordinal)
    {
        "new", "is", "as", "typeof", "sizeof", "default", "catch",
    };

    private const string TypeArgumentCharacters = "_@.,?[] \t\r\n";

    private readonly CSharpSource source;
    private readonly bool[] declarative;

    private CSharpReferenceScanner(CSharpSource source)
    {
        this.source = source;
        declarative = new bool[source.Masked.Length];
        MarkDeclarativeRegions();
    }

    public static List<NameOccurrence> Scan(CSharpSource source, Func<string, bool> isKnown)
        => new CSharpReferenceScanner(source).Read(isKnown);

    private List<NameOccurrence> Read(Func<string, bool> isKnown)
    {
        var text = source.Masked;
        var found = new List<NameOccurrence>();
        var previousWord = string.Empty;
        var previousEnd = -1;

        for (var index = 0; index < text.Length;)
        {
            if (!IsNameStart(text, index))
            {
                index++;
                continue;
            }

            var end = EndOfName(text, index);
            var name = text[index..end];
            if (isKnown(name))
            {
                found.Add(new NameOccurrence(
                    name, source.LineOf(index), GradeOf(index, end, previousWord, previousEnd)));
            }

            previousWord = name;
            previousEnd = end;
            index = end;
        }

        return found;
    }

    private EvidenceGrade GradeOf(int start, int end, string previousWord, int previousEnd)
    {
        var text = source.Masked;
        var previous = PreviousVisible(start);
        if (previous >= 0 && text[previous] == '.')
        {
            return EvidenceGrade.Inferred;
        }

        if (previousEnd >= 0 && OnlySeparators(previousEnd, start) && DeclarativeKeywords.Contains(previousWord))
        {
            return EvidenceGrade.Proven;
        }

        if (declarative[start])
        {
            return EvidenceGrade.Proven;
        }

        var next = AfterTypeSuffix(end);
        return next >= 0 && IsNameStart(text, next) ? EvidenceGrade.Proven : EvidenceGrade.Inferred;
    }

    /// <summary>
    /// Past a nullable mark and any array ranks, so that `Store? held` and `Store[] held` are
    /// read the same way `Store held` is: a type followed by the name it introduces.
    /// </summary>
    private int AfterTypeSuffix(int end)
    {
        var text = source.Masked;
        var index = NextVisible(end);
        while (index >= 0 && index < text.Length)
        {
            if (text[index] == '?')
            {
                index = NextVisible(index + 1);
                continue;
            }

            if (text[index] == '[' && NextVisible(index + 1) is var close && close >= 0 && text[close] == ']')
            {
                index = NextVisible(close + 1);
                continue;
            }

            return index;
        }

        return -1;
    }

    /// <summary>
    /// A keyword may be separated from the type it introduces by an opening parenthesis, as
    /// in `typeof(` and `catch (`, and by nothing else.
    /// </summary>
    private bool OnlySeparators(int from, int to)
    {
        var text = source.Masked;
        for (var index = from; index < to; index++)
        {
            if (!char.IsWhiteSpace(text[index]) && text[index] != '(')
            {
                return false;
            }
        }

        return true;
    }

    private void MarkDeclarativeRegions()
    {
        var text = source.Masked;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '<' && index > 0 && IsNameCharacter(text[index - 1]))
            {
                Mark(index + 1, TypeArgumentEnd(index));
                continue;
            }

            if (text[index] == '[' && StartsLine(index))
            {
                Mark(index + 1, BracketEnd(index));
            }
        }
    }

    private void Mark(int from, int to)
    {
        for (var index = from; index >= 0 && index < to; index++)
        {
            declarative[index] = true;
        }
    }

    /// <summary>
    /// A run that holds nothing but names, separators and nested angle brackets is a type
    /// argument list. Anything else — an operator, a call, a literal — makes it a comparison.
    /// </summary>
    private int TypeArgumentEnd(int open)
    {
        var text = source.Masked;
        var depth = 1;
        for (var index = open + 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '>' && --depth == 0)
            {
                return index;
            }

            if (character == '<')
            {
                depth++;
            }
            else if (character != '>'
                && !char.IsLetterOrDigit(character)
                && !TypeArgumentCharacters.Contains(character, StringComparison.Ordinal))
            {
                return -1;
            }
        }

        return -1;
    }

    private int BracketEnd(int open)
    {
        var text = source.Masked;
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            if (text[index] == '[')
            {
                depth++;
            }
            else if (text[index] == ']' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private bool StartsLine(int index)
    {
        var text = source.Masked;
        for (var scan = index - 1; scan >= 0 && text[scan] != '\n'; scan--)
        {
            if (!char.IsWhiteSpace(text[scan]))
            {
                return false;
            }
        }

        return true;
    }

    private int PreviousVisible(int index)
    {
        var text = source.Masked;
        var scan = index - 1;
        while (scan >= 0 && char.IsWhiteSpace(text[scan]))
        {
            scan--;
        }

        return scan;
    }

    private int NextVisible(int index)
    {
        var text = source.Masked;
        var scan = index;
        while (scan < text.Length && char.IsWhiteSpace(text[scan]))
        {
            scan++;
        }

        return scan < text.Length ? scan : -1;
    }

    private static int EndOfName(string text, int start)
    {
        var end = start;
        while (end < text.Length && IsNameCharacter(text[end]))
        {
            end++;
        }

        return end;
    }

    private static bool IsNameStart(string text, int index)
        => (char.IsLetter(text[index]) || text[index] is '_' or '@')
            && (index == 0 || !IsNameCharacter(text[index - 1]));

    private static bool IsNameCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '@';
}
