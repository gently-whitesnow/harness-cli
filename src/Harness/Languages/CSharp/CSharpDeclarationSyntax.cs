namespace Harness.Languages.CSharp;

/// <summary>
/// Reads one declaration header — the masked text between the previous terminator and the
/// `{` or `;` that ends it. Everything here is lexical: it recognizes the forms the metrics
/// need and returns nothing for the rest, so an unfamiliar construct costs a measurement
/// rather than producing a wrong one.
/// </summary>
internal static class CSharpDeclarationSyntax
{
    private static readonly string[] TypeKeywords = ["class", "struct", "interface", "record", "enum"];

    private static readonly HashSet<string> NotDeclarationNames = new(StringComparer.Ordinal)
    {
        "if", "for", "foreach", "while", "do", "switch", "case", "catch", "try", "finally", "using", "lock",
        "fixed", "return", "else", "when", "new", "yield", "await", "throw", "checked", "unchecked", "unsafe",
        "nameof", "typeof", "sizeof", "default", "is", "as", "in", "out", "ref", "stackalloc", "base", "this",
        "and", "or", "not", "with", "from", "select", "where", "let", "goto", "delegate",
    };

    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "internal", "file", "static", "sealed", "abstract", "virtual",
        "override", "async", "partial", "extern", "unsafe", "readonly", "required", "const", "volatile",
        "event", "void", "implicit", "explicit", "operator",
    };

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

            var close = CloseOfBracket(header, index);
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

    // Skipping groups without a preceding name prevents tuple return types from becoming
    // parameter lists and their modifiers from becoming member names.
    public static MemberSignature? SignatureOf(string text)
    {
        var declaration = WithoutConstraints(text);

        for (var from = 0; from < declaration.Length;)
        {
            var open = TopLevelIndexOf(declaration, "(", from);
            if (open < 0)
            {
                return null;
            }

            var prefix = declaration[..open].TrimEnd();
            if (HasAssignment(prefix))
            {
                return null;
            }

            var generic = prefix.LastIndexOf('<');
            if (generic >= 0 && prefix.EndsWith('>'))
            {
                prefix = prefix[..generic].TrimEnd();
            }

            var name = LastName(prefix);
            if (name is not null && !NotDeclarationNames.Contains(name) && !Modifiers.Contains(name))
            {
                return new MemberSignature(name, open);
            }

            from = EndOfGroup(declaration, open);
        }

        return null;
    }

    /// <summary>
    /// The name a field or property declaration introduces: the last identifier of a header
    /// that carries a type and a name and opens no parameter list.
    /// </summary>
    public static string? FieldNameOf(string text)
    {
        var declaration = WithoutInitializer(WithoutConstraints(text));
        if (TopLevelIndexOf(declaration, "(") >= 0)
        {
            return null;
        }

        var name = LastName(declaration);
        if (name is null || NotDeclarationNames.Contains(name) || Modifiers.Contains(name))
        {
            return null;
        }

        var remainder = declaration[..declaration.LastIndexOf(name, StringComparison.Ordinal)].Trim();
        return remainder.Length == 0 ? null : name;
    }

    /// <summary>The types a declaration names after `:`, before any constraint clause.</summary>
    public static string BaseListOf(string text)
    {
        var declaration = WithoutConstraints(text);
        var colon = TopLevelIndexOf(declaration, ":");
        return colon < 0 ? string.Empty : declaration[(colon + 1)..];
    }

    public static int ParameterCountOf(string text)
        => ParameterCountOf(text, TopLevelIndexOf(text, "(", 0));

    public static int ParameterCountOf(string text, int open)
    {
        if (open < 0)
        {
            return -1;
        }

        var parameters = 1;
        var depth = 0;
        var angle = 0;
        for (var index = open + 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    return text[(open + 1)..index].Trim().Length == 0 ? 0 : parameters;
                }

                depth--;
            }
            else if (character is '(' or '[' or '{')
            {
                depth++;
            }
            else if (character == '<')
            {
                // Generic arguments carry commas that are not parameter separators.
                angle++;
            }
            else if (character == '>' && angle > 0 && text[index - 1] is not ('=' or '-'))
            {
                angle--;
            }
            else if (character == ',' && depth == 0 && angle == 0)
            {
                parameters++;
            }
        }

        return -1;
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

    private static string WithoutInitializer(string text)
    {
        var assignment = TopLevelIndexOf(text, "=");
        return assignment >= 0 ? text[..assignment].TrimEnd() : text;
    }

    private static bool HasAssignment(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '=' && !IsComparison(text, index, "="))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsComparison(string text, int index, string target)
        => target == "="
            && ((index > 0 && text[index - 1] is '=' or '!' or '<' or '>')
                || (index + 1 < text.Length && text[index + 1] is '=' or '>'));

    private static bool IsQualifiedNameSeparator(string text, int index, string target)
        => target == ":"
            && (text.AsSpan(index).StartsWith("::", StringComparison.Ordinal)
                || (index > 0 && text[index - 1] == ':'));

    private static string? LastName(string text)
    {
        var end = text.Length;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '@' or '.'))
        {
            start--;
        }

        if (start == end)
        {
            return null;
        }

        var name = text[start..end];
        var dot = name.LastIndexOf('.');
        var last = dot >= 0 ? name[(dot + 1)..] : name;
        return IsIdentifier(last) ? last : null;
    }

    private static int EndOfGroup(string text, int open)
    {
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            if (text[index] is '(' or '[' or '{')
            {
                depth++;
            }
            else if (text[index] is ')' or ']' or '}' && --depth == 0)
            {
                return index + 1;
            }
        }

        return text.Length;
    }

    private static int CloseOfBracket(string text, int open)
    {
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
}
