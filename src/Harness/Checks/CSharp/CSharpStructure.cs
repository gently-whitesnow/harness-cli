namespace Harness.Checks.CSharp;

internal enum DeclarationKind
{
    Type,

    Constructor,

    Method,
}

internal sealed class Declaration
{
    public required DeclarationKind Kind { get; init; }

    public required string Subject { get; init; }

    public required int FirstLine { get; init; }

    public int LastLine { get; set; }

    public int ParameterCount { get; set; } = -1;

    public int PublicMembers { get; set; }

    public bool IsComplete => LastLine >= FirstLine;
}

internal sealed record CSharpStructure(IReadOnlyList<Declaration> Declarations, int UsingDirectives);

internal sealed record MemberSignature(string Name, int ParameterList);

/// <summary>
/// Reads declarations out of masked C# by matching braces and reading the text that
/// precedes them. This is a lexical reader, not a compiler front end: it recognizes the
/// declaration forms the metrics need and treats everything else as an ordinary block, so
/// an unfamiliar construct costs a measurement rather than producing a wrong one.
/// </summary>
internal sealed class CSharpStructureReader
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

    private readonly CSharpSource source;
    private readonly List<Declaration> declarations = [];
    private readonly List<Scope> scopes = [new Scope()];
    private readonly List<string> qualifiers = [];
    private int typeDepth;
    private int usingDirectives;

    private CSharpStructureReader(CSharpSource source) => this.source = source;

    public static CSharpStructure Read(CSharpSource source)
    {
        var reader = new CSharpStructureReader(source);
        reader.Walk();
        return new CSharpStructure(
            reader.declarations.Where(declaration => declaration.IsComplete).ToList(),
            reader.usingDirectives);
    }

    private void Walk()
    {
        var text = source.Masked;
        var headerStart = -1;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is not ('{' or '}' or ';'))
            {
                if (headerStart < 0 && !char.IsWhiteSpace(character))
                {
                    headerStart = index;
                }

                continue;
            }

            var header = headerStart < 0 ? string.Empty : text[headerStart..index];
            var headerIndex = headerStart;
            headerStart = -1;

            switch (character)
            {
                case '{':
                    OpenBrace(header, headerIndex);
                    break;

                case '}':
                    CloseBrace(index);
                    break;

                default:
                    Terminate(header, headerIndex, index);
                    break;
            }
        }
    }

    private void OpenBrace(string header, int headerIndex)
    {
        var (text, offset) = WithoutAttributes(header);
        var parent = scopes[^1];
        var scope = new Scope();

        if (typeDepth == 0 && NamespaceNameOf(text) is { } namespaceName)
        {
            scope.QualifiesName = true;
            qualifiers.Add(namespaceName);
            scopes.Add(scope);
            return;
        }

        var (declaration, typeName) = Recognize(parent, text, headerIndex + offset);
        scope.Declaration = declaration;

        if (typeName is not null)
        {
            scope.TypeName = typeName;
            scope.QualifiesName = true;
            qualifiers.Add(typeName);
            typeDepth++;
        }

        scopes.Add(scope);
    }

    private void CloseBrace(int index)
    {
        if (scopes.Count <= 1)
        {
            return;
        }

        var scope = scopes[^1];
        scopes.RemoveAt(scopes.Count - 1);

        if (scope.Declaration is not null)
        {
            scope.Declaration.LastLine = source.LineOf(index);
            scope.Declaration.PublicMembers = scope.PublicMembers;
        }

        if (scope.TypeName is not null)
        {
            typeDepth--;
        }

        if (scope.QualifiesName)
        {
            qualifiers.RemoveAt(qualifiers.Count - 1);
        }
    }

    private void Terminate(string header, int headerIndex, int index)
    {
        var (text, offset) = WithoutAttributes(header);
        if (text.Length == 0)
        {
            return;
        }

        if (typeDepth == 0)
        {
            // A `using` inside a member is a statement, not an import; inside a type there
            // are no directives, so the enclosing type depth is what tells them apart.
            if (IsUsingDirective(text))
            {
                usingDirectives++;
                return;
            }

            if (NamespaceNameOf(text) is { } namespaceName)
            {
                // A file-scoped namespace qualifies everything that follows it in the file.
                qualifiers.Add(namespaceName);
                return;
            }
        }

        // A declaration that ends at a semicolon — a positional record, an expression-bodied
        // member, an abstract or interface member — spans only the lines of its own text.
        var (declaration, _) = Recognize(scopes[^1], text, headerIndex + offset);
        if (declaration is not null)
        {
            declaration.LastLine = source.LineOf(index);
        }
    }

    private (Declaration? Declaration, string? TypeName) Recognize(Scope parent, string text, int index)
    {
        CountPublicMember(parent, text);

        if (TypeNameOf(text) is { } typeName)
        {
            var type = Declare(DeclarationKind.Type, Qualify(typeName), index);
            type.ParameterCount = ParameterCountOf(WithoutConstraintsAndBaseList(text));
            return (type, typeName);
        }

        if (parent.TypeName is null || SignatureOf(text) is not { } signature)
        {
            return (null, null);
        }

        var isConstructor = string.Equals(signature.Name, parent.TypeName, StringComparison.Ordinal);
        var member = Declare(
            isConstructor ? DeclarationKind.Constructor : DeclarationKind.Method,
            isConstructor ? Qualify() : Qualify(signature.Name),
            index);

        // Only a constructor's arity is measured, so only a constructor's is read.
        if (isConstructor)
        {
            member.ParameterCount = ParameterCountOf(text, signature.ParameterList);
        }

        return (member, null);
    }

    private Declaration Declare(DeclarationKind kind, string subject, int index)
    {
        var declaration = new Declaration
        {
            Kind = kind,
            Subject = subject,
            FirstLine = source.LineOf(index),
        };

        declarations.Add(declaration);
        return declaration;
    }

    private static void CountPublicMember(Scope parent, string text)
    {
        if (parent.TypeName is not null && StartsWithWord(text, "public"))
        {
            parent.PublicMembers++;
        }
    }

    private string Qualify(string? name = null)
        => string.Join('.', name is null ? qualifiers : qualifiers.Append(name));

    private static bool IsUsingDirective(string text)
        => StartsWithWord(text, "using")
            || (StartsWithWord(text, "global") && StartsWithWord(text["global".Length..].TrimStart(), "using"));

    private static string? NamespaceNameOf(string text)
    {
        if (!StartsWithWord(text, "namespace"))
        {
            return null;
        }

        var name = text["namespace".Length..].Trim();
        return name.Length == 0 ? null : name;
    }

    private static string? TypeNameOf(string text)
    {
        var declaration = WithoutConstraintsAndBaseList(text);
        var end = declaration.AsSpan().IndexOfAny('(', '<', '[');
        var tokens = (end < 0 ? declaration : declaration[..end])
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var keyword = Array.FindIndex(tokens, TypeKeywords.Contains);
        if (keyword < 0)
        {
            return null;
        }

        // `record class` and `record struct` place a second keyword before the name.
        var name = tokens.Skip(keyword + 1).FirstOrDefault(token => !TypeKeywords.Contains(token));
        return name is not null && IsIdentifier(name) ? name : null;
    }

    // Skipping groups without a preceding name prevents tuple return types from becoming
    // parameter lists and their modifiers from becoming member names.
    private static MemberSignature? SignatureOf(string text)
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

    private static int ParameterCountOf(string text)
        => ParameterCountOf(text, TopLevelIndexOf(text, "(", 0));

    private static int ParameterCountOf(string text, int open)
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

    private static string WithoutConstraints(string text)
    {
        var constraints = TopLevelIndexOf(text, " where ");
        return constraints >= 0 ? text[..constraints] : text;
    }

    private static string WithoutConstraintsAndBaseList(string text)
    {
        var declaration = WithoutConstraints(text);
        var colon = TopLevelIndexOf(declaration, ":");
        return colon >= 0 ? declaration[..colon] : declaration;
    }

    private static bool HasAssignment(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '=')
            {
                continue;
            }

            var isComparison = (index > 0 && text[index - 1] is '=' or '!' or '<' or '>')
                || (index + 1 < text.Length && text[index + 1] is '=' or '>');

            if (!isComparison)
            {
                return true;
            }
        }

        return false;
    }

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

    private static bool IsIdentifier(string token)
        => token.Length > 0
            && (char.IsLetter(token[0]) || token[0] is '_' or '@')
            && token.All(character => char.IsLetterOrDigit(character) || character is '_' or '@');

    private static bool StartsWithWord(string text, string word)
        => text.Length > word.Length
            && text.StartsWith(word, StringComparison.Ordinal)
            && char.IsWhiteSpace(text[word.Length]);

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

    private static int TopLevelIndexOf(string text, string target, int from = 0)
    {
        var depth = 0;
        for (var index = from; index + target.Length <= text.Length; index++)
        {
            if (depth == 0
                && text.AsSpan(index).StartsWith(target, StringComparison.Ordinal)
                && !IsQualifiedNameSeparator(text, index, target))
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

    private static bool IsQualifiedNameSeparator(string text, int index, string target)
        => target == ":"
            && (text.AsSpan(index).StartsWith("::", StringComparison.Ordinal)
                || (index > 0 && text[index - 1] == ':'));

    private static (string Text, int Offset) WithoutAttributes(string header)
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

    private sealed class Scope
    {
        public string? TypeName { get; set; }

        public bool QualifiesName { get; set; }

        public Declaration? Declaration { get; set; }

        public int PublicMembers { get; set; }
    }
}
