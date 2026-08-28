using Harness.Languages.CSharp;

namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>
/// Reads one declaration header — the masked text between the previous terminator and the
/// `{` or `;` that ends it. Everything here is lexical and recognizes only the forms the
/// dependency graph needs.
/// </summary>
internal static class CSharpDeclarationSyntax
{
    private static readonly string[] TypeKeywords = ["class", "struct", "interface", "record", "enum"];

    /// <summary>
    /// An expression body is the member's implementation, not part of its header. Cutting it
    /// away keeps a call inside it from being read as the declaration itself.
    /// </summary>
    public static string WithoutExpressionBody(string text)
    {
        var arrow = TopLevelIndexOf(text, "=>");
        return arrow >= 0 ? text[..arrow].TrimEnd() : text;
    }

    public static (string Text, int Offset) WithoutAttributes(string header)
    {
        var index = 0;
        while (true)
        {
            while (index < header.Length && char.IsWhiteSpace(header[index]))
            {
                index++;
            }

            if (index >= header.Length || header[index] != '[')
            {
                break;
            }

            var close = CSharpBrackets.CloseOf(header, index);
            if (close < 0)
            {
                break;
            }

            index = close + 1;
        }

        return (header[index..].Trim(), index);
    }

    public static bool IsUsingDirective(string text)
        => StartsWithWord(text, "using")
            || (StartsWithWord(text, "global") && StartsWithWord(text["global".Length..].TrimStart(), "using"));

    /// <summary>
    /// The namespace a `using` directive imports. An alias names one type or namespace under
    /// a local name, so what it imports is the right-hand side; `using static` imports the
    /// members of the type it names, so the type itself is what the file depends on.
    /// </summary>
    public static string? ImportNameOf(string text)
    {
        var name = text.TrimStart();
        if (StartsWithWord(name, "global"))
        {
            name = name["global".Length..].TrimStart();
        }

        if (!StartsWithWord(name, "using"))
        {
            return null;
        }

        name = name["using".Length..].Trim();
        if (StartsWithWord(name, "static"))
        {
            name = name["static".Length..].Trim();
        }

        var alias = TopLevelIndexOf(name, "=");
        if (alias >= 0)
        {
            name = name[(alias + 1)..].Trim();
        }

        return name.Length == 0 ? null : name;
    }

    public static string? NamespaceNameOf(string text)
    {
        if (!StartsWithWord(text, "namespace"))
        {
            return null;
        }

        var name = text["namespace".Length..].Trim();
        return name.Length == 0 ? null : name;
    }

    public static RecognizedType? TypeOf(string text)
    {
        var declaration = WithoutConstraintsAndBaseList(text);
        var end = declaration.AsSpan().IndexOfAny('(', '<', '[');
        var tokens = (end < 0 ? declaration : declaration[..end])
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var keywordIndex = Array.FindIndex(tokens, TypeKeywords.Contains);
        if (keywordIndex < 0)
        {
            return null;
        }

        // `record class` and `record struct` place a second keyword before the name.
        var keyword = tokens[keywordIndex];
        var name = tokens.Skip(keywordIndex + 1).FirstOrDefault(token => !TypeKeywords.Contains(token));
        if (name is null || !IsIdentifier(name))
        {
            return null;
        }

        return new RecognizedType(
            name,
            keyword == "record" ? TypeForm.Record : keyword == "class" ? TypeForm.Class : TypeForm.Other);
    }

    /// <summary>The types a declaration names after `:`, before any constraint clause.</summary>
    public static string BaseListOf(string text)
    {
        var declaration = WithoutConstraints(text);
        var colon = TopLevelIndexOf(declaration, ":");
        return colon < 0 ? string.Empty : declaration[(colon + 1)..];
    }

    public static string WithoutConstraints(string text)
    {
        var constraints = TopLevelIndexOf(text, " where ");
        return constraints >= 0 ? text[..constraints] : text;
    }

    public static string WithoutConstraintsAndBaseList(string text)
    {
        var declaration = WithoutConstraints(text);
        var colon = TopLevelIndexOf(declaration, ":");
        return colon >= 0 ? declaration[..colon] : declaration;
    }

    public static bool IsIdentifier(string token)
        => token.Length > 0
            && (char.IsLetter(token[0]) || token[0] is '_' or '@')
            && token.All(character => char.IsLetterOrDigit(character) || character is '_' or '@');

    public static bool StartsWithWord(string text, string word)
        => text.Length > word.Length
            && text.StartsWith(word, StringComparison.Ordinal)
            && char.IsWhiteSpace(text[word.Length]);

    public static int TopLevelIndexOf(string text, string target, int from = 0)
    {
        var depth = 0;
        for (var index = from; index + target.Length <= text.Length; index++)
        {
            if (depth == 0
                && text.AsSpan(index).StartsWith(target, StringComparison.Ordinal)
                && !IsQualifiedNameSeparator(text, index, target)
                && !IsComparison(text, index, target))
            {
                return index;
            }

            var character = text[index];
            if (character is '(' or '[' or '{')
            {
                depth++;
            }
            else if (character is ')' or ']' or '}')
            {
                depth--;
            }
        }

        return -1;
    }

    private static bool IsComparison(string text, int index, string target)
        => target == "="
            && ((index > 0 && text[index - 1] is '=' or '!' or '<' or '>')
                || (index + 1 < text.Length && text[index + 1] is '=' or '>'));

    private static bool IsQualifiedNameSeparator(string text, int index, string target)
        => target == ":"
            && (text.AsSpan(index).StartsWith("::", StringComparison.Ordinal)
                || (index > 0 && text[index - 1] == ':'));

}
