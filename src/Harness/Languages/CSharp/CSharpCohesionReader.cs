using Harness.Structure;

namespace Harness.Languages.CSharp;

/// <summary>
/// Collects, for each declared type, the members it holds and which other members of the same
/// type each one names. Constructors are left out: a constructor touches everything a type
/// holds by definition, so counting it would connect every group to every other one and hide
/// exactly what this measurement is looking for. A property with an expression body computes
/// its result rather than holding one, so it counts as behaviour and not as state.
/// </summary>
internal static class CSharpCohesionReader
{
    public static List<TypeCohesion> Read(IReadOnlyList<CSharpFile> files)
    {
        var types = new List<TypeCohesion>();
        foreach (var file in files)
        {
            types.AddRange(TypesIn(file));
        }

        return types;
    }

    private static IEnumerable<TypeCohesion> TypesIn(CSharpFile file)
    {
        var owned = file.Structure.Declarations
            .Where(declaration => declaration.Kind is DeclarationKind.Method or DeclarationKind.Field)
            .Where(declaration => declaration.Owner is not null)
            .GroupBy(declaration => declaration.Owner!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var type in file.Types)
        {
            if (owned.TryGetValue(type.Subject, out var members))
            {
                yield return new TypeCohesion(
                    type.Subject, file.Path, type.FirstLine, Members(file.Source, members));
            }
        }
    }

    private static List<CohesionMember> Members(CSharpSource source, List<Declaration> declared)
    {
        var names = declared.Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
        var members = new Dictionary<string, CohesionMember>(StringComparer.Ordinal);

        foreach (var declaration in declared)
        {
            var mentions = Mentions(
                source.TextBetween(declaration.FirstLine, declaration.LastLine), names, declaration.Name);

            // Overloads are one name and therefore one member: what they share is the state
            // they touch, and that is all this measurement reads.
            if (members.TryGetValue(declaration.Name, out var existing))
            {
                mentions.UnionWith(existing.Mentions);
            }

            var isState = declaration.Kind == DeclarationKind.Field
                && !declaration.HasExpressionBody
                && (existing is null || existing.IsState);
            members[declaration.Name] = new CohesionMember(declaration.Name, isState, [.. mentions]);
        }

        return [.. members.Values];
    }

    private static HashSet<string> Mentions(ReadOnlySpan<char> text, HashSet<string> names, string self)
    {
        var mentions = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < text.Length;)
        {
            if (!IsNameStart(text, index))
            {
                index++;
                continue;
            }

            var end = index;
            while (end < text.Length && IsNameCharacter(text[end]))
            {
                end++;
            }

            var name = text[index..end].ToString();
            if (!string.Equals(name, self, StringComparison.Ordinal) && names.Contains(name))
            {
                mentions.Add(name);
            }

            index = end;
        }

        return mentions;
    }

    private static bool IsNameStart(ReadOnlySpan<char> text, int index)
        => (char.IsLetter(text[index]) || text[index] is '_' or '@')
            && (index == 0 || !IsNameCharacter(text[index - 1]));

    private static bool IsNameCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '@';
}
