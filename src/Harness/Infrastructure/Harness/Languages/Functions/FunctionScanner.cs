using System.Text.RegularExpressions;
using Harness.Languages.Functions;

namespace Harness.Infrastructure.Languages.Functions;

/// <summary>Finds callable brace spans in masked source and counts lines owned by each span.</summary>
internal static partial class FunctionScanner
{
    private static readonly HashSet<string> Control =
        ["if", "for", "foreach", "while", "switch", "catch", "lock", "using", "fixed", "else", "do",
         "try", "finally", "select", "case", "default"];

    private static readonly string[] CognitiveBranches =
        ["if", "for", "foreach", "while", "catch", "switch", "select"];

    public static IReadOnlyList<FunctionUnit> Read(string path, string masked, bool go)
    {
        var (nodes, collections) = ParseNodes(masked, go);
        var complete = nodes.Where(node => node.End > node.Open).ToList();
        var lines = LineStarts(masked);
        var units = new List<FunctionUnit>();
        foreach (var node in complete.Where(node => node.Kind == Kind.Function))
        {
            var excluded = complete
                .Where(child => child != node && child.Start >= node.Open && child.End <= node.End)
                .Where(child => child.Kind is Kind.Function or Kind.Data)
                .Where(child => !HasExcludedAncestor(child, node))
                .Select(child => child.Kind == Kind.Function
                    ? (child.Start, child.End)
                    : DataSpan(child, masked))
                .Concat(collections.Where(span => span.From >= node.Open && span.To <= node.End))
                .ToList();
            var own = Count(masked, lines, node.Start, node.End, excluded);
            var logical = Count(masked, lines, node.Start, node.End, []);
            var largest = complete
                .Where(child => child.Kind == Kind.Function && child != node && child.Start >= node.Open && child.End <= node.End)
                .Select(child => Count(masked, lines, child.Start, child.End, []))
                .DefaultIfEmpty(0).Max();
            units.Add(new FunctionUnit(path, node.Name, LineOf(lines, node.Start), own, logical, largest,
                Cognitive(node, complete, masked, excluded)));
        }

        if (!go && !complete.Any(node => node.Kind == Kind.Type) && masked.AsSpan().ContainsAnyExcept(" \t\r\n"))
        {
            var excluded = complete.Where(node => node.Kind is Kind.Function or Kind.Data)
                .Where(node => !node.Ancestors().Any(parent => parent.Kind is Kind.Function or Kind.Data))
                .Select(node => node.Kind == Kind.Function
                    ? (node.Start, node.End)
                    : DataSpan(node, masked))
                .Concat(collections)
                .ToList();
            units.Add(new FunctionUnit(path, "<top-level>", 1,
                Count(masked, lines, 0, masked.Length, excluded),
                Count(masked, lines, 0, masked.Length, []),
                complete.Where(node => node.Kind == Kind.Function)
                    .Select(node => Count(masked, lines, node.Start, node.End, []))
                    .DefaultIfEmpty(0).Max(),
                Cognitive(null, complete, masked, excluded)));
        }

        return units;
    }

    private static (List<Node> Nodes, List<(int From, int To)> Collections) ParseNodes(string masked, bool go)
    {
        var nodes = new List<Node>();
        var stack = new Stack<Node>();
        var brackets = new Stack<(int Start, bool Data)>();
        var collections = new List<(int From, int To)>();
        var boundary = 0;
        var parentheses = 0;
        for (var index = 0; index < masked.Length; index++)
        {
            if (masked[index] == '(')
            {
                parentheses++;
            }
            else if (masked[index] == ')')
            {
                parentheses = Math.Max(0, parentheses - 1);
            }
            else if (!go && masked[index] == '[')
            {
                var before = masked.AsSpan(0, index).TrimEnd();
                var data = before.EndsWith('=') || before.EndsWith("return", StringComparison.Ordinal);
                brackets.Push((index, data));
            }
            else if (!go && masked[index] == ']' && brackets.TryPop(out var bracket))
            {
                if (bracket.Data)
                {
                    collections.Add((bracket.Start + 1, index));
                }
            }
            else if (masked[index] == '{')
            {
                var start = boundary;
                while (start < index && char.IsWhiteSpace(masked[start]))
                {
                    start++;
                }

                var header = masked[start..index].TrimEnd();
                var kind = Classify(header, go, out var name);
                if (go && kind == Kind.Function)
                {
                    start += GoFunction().Match(header).Index;
                }
                var node = new Node(start, index, kind, name);
                if (stack.TryPeek(out var parent))
                {
                    node.Parent = parent;
                    parent.Children.Add(node);
                }

                nodes.Add(node);
                stack.Push(node);
                boundary = index + 1;
            }
            else if (masked[index] == '}')
            {
                if (stack.TryPop(out var node))
                {
                    node.End = index + 1;
                }

                boundary = index + 1;
            }
            else if (masked[index] == ';' && parentheses == 0)
            {
                boundary = index + 1;
            }
        }

        if (!go)
        {
            ScanExpressionFunctions(masked, nodes);
        }

        return (nodes, collections);
    }

    private static void ScanExpressionFunctions(string masked, List<Node> nodes)
    {
        for (var arrow = 0; arrow + 1 < masked.Length; arrow++)
        {
            if (masked[arrow] != '=' || masked[arrow + 1] != '>')
            {
                continue;
            }

            var body = arrow + 2;
            while (body < masked.Length && char.IsWhiteSpace(masked[body]))
            {
                body++;
            }

            if (body >= masked.Length || masked[body] == '{')
            {
                continue;
            }

            var containing = nodes.Where(node => node.End > body && node.Open < arrow)
                .OrderBy(node => node.End - node.Start).FirstOrDefault();
            if (containing is { Kind: Kind.Data }
                && masked[containing.Start..containing.Open].TrimEnd().EndsWith("switch", StringComparison.Ordinal))
            {
                continue;
            }

            var end = ExpressionEnd(masked, body);
            if (end <= body)
            {
                continue;
            }

            var (start, name) = ArrowSignature(masked, arrow);
            var unit = new Node(start, arrow, Kind.Function, name) { End = end };
            unit.Parent = nodes.Where(node => node != unit && node.Start <= start && node.End >= end)
                .OrderBy(node => node.End - node.Start).FirstOrDefault();
            nodes.Add(unit);
        }
    }

    private static (int Start, string Name) ArrowSignature(string masked, int arrow)
    {
        var cursor = arrow - 1;
        while (cursor >= 0 && char.IsWhiteSpace(masked[cursor]))
        {
            cursor--;
        }

        if (cursor >= 0 && masked[cursor] == ')')
        {
            var depth = 1;
            var open = cursor - 1;
            for (; open >= 0; open--)
            {
                if (masked[open] == ')')
                {
                    depth++;
                }

                if (masked[open] == '(' && --depth == 0)
                {
                    break;
                }
            }

            if (open >= 0)
            {
                var boundary = masked.LastIndexOfAny(['{', '}', ';'], open) + 1;
                var header = masked[boundary..arrow].Trim();
                var method = CSharpMethod().Match(header);
                if (method.Success && !header.Contains('='))
                {
                    while (boundary < arrow && char.IsWhiteSpace(masked[boundary]))
                    {
                        boundary++;
                    }

                    return (boundary, method.Groups["name"].Value);
                }

                return (open, "<lambda>");
            }
        }

        var start = cursor;
        while (start >= 0 && (char.IsLetterOrDigit(masked[start]) || masked[start] == '_'))
        {
            start--;
        }

        return (start + 1, "<lambda>");
    }

    private static int ExpressionEnd(string masked, int body)
    {
        var parens = 0;
        var brackets = 0;
        var braces = 0;
        for (var index = body; index < masked.Length; index++)
        {
            switch (masked[index])
            {
                case '(':
                    parens++;
                    break;
                case '[':
                    brackets++;
                    break;
                case '{':
                    braces++;
                    break;
                case ')':
                    if (parens == 0)
                    {
                        return index;
                    }

                    parens--;
                    break;
                case ']':
                    if (brackets == 0)
                    {
                        return index;
                    }

                    brackets--;
                    break;
                case '}':
                    if (braces == 0)
                    {
                        return index;
                    }

                    braces--;
                    break;
                case ',' or ';' when parens == 0 && brackets == 0 && braces == 0:
                    return index;
            }
        }

        return masked.Length;
    }

    private static bool HasExcludedAncestor(Node child, Node owner)
        => child.Ancestors().TakeWhile(parent => parent != owner)
            .Any(parent => parent.Kind is Kind.Function or Kind.Data);

    private static (int From, int To) DataSpan(Node node, string masked)
    {
        var end = node.End;
        while (end < masked.Length && masked[end] is ' ' or '\t')
        {
            end++;
        }

        if (end < masked.Length && masked[end] is ',' or ';')
        {
            end++;
        }

        return (node.Open, end);
    }

    private static int Cognitive(
        Node? owner,
        IReadOnlyList<Node> nodes,
        string masked,
        IReadOnlyList<(int From, int To)> excluded)
    {
        var from = owner?.Open + 1 ?? 0;
        var to = owner?.End - 1 ?? masked.Length;
        var score = 0;
        foreach (var node in nodes.Where(node => node.Kind == Kind.Block && node.Start >= from && node.End <= to))
        {
            if (node.Ancestors().TakeWhile(parent => parent != owner)
                .Any(parent => parent.Kind is Kind.Function or Kind.Data))
            {
                continue;
            }

            var header = masked[node.Start..node.Open].TrimStart();
            if (!IsCognitiveBranch(header))
            {
                continue;
            }

            var depth = node.Ancestors().TakeWhile(parent => parent != owner)
                .Count(parent => parent.Kind == Kind.Block
                    && IsCognitiveBranch(masked[parent.Start..parent.Open].TrimStart()));
            score += 1 + depth;
        }

        for (var index = from; index + 1 < to; index++)
        {
            if (excluded.Any(span => index >= span.From && index < span.To))
            {
                continue;
            }

            if (masked.AsSpan(index).StartsWith("&&", StringComparison.Ordinal)
                || masked.AsSpan(index).StartsWith("||", StringComparison.Ordinal))
            {
                score++;
                index++;
            }
        }

        return score;
    }

    private static bool IsCognitiveBranch(string header)
        => header.StartsWith("else if", StringComparison.Ordinal)
            || CognitiveBranches.Any(keyword => CSharpWord(header, keyword));

    private static bool CSharpWord(string text, string word)
        => text.StartsWith(word, StringComparison.Ordinal)
            && (text.Length == word.Length || char.IsWhiteSpace(text[word.Length]) || text[word.Length] == '(');

    private static Kind Classify(string header, bool go, out string name)
    {
        name = string.Empty;
        if (go)
        {
            var function = GoFunction().Match(header);
            if (function.Success)
            {
                name = function.Groups["name"].Success ? function.Groups["name"].Value : "<func literal>";
                return Kind.Function;
            }

            return IsControl(header) ? Kind.Block : Kind.Data;
        }

        if (CSharpType().IsMatch(header))
        {
            return Kind.Type;
        }

        if (IsControl(header))
        {
            return Kind.Block;
        }

        if (header.Contains("=>", StringComparison.Ordinal))
        {
            name = "<lambda>";
            return Kind.Function;
        }

        var method = CSharpMethod().Match(header);
        if (method.Success && !Control.Contains(method.Groups["name"].Value)
            && !header.Contains("= new ", StringComparison.Ordinal))
        {
            name = method.Groups["name"].Value;
            return Kind.Function;
        }

        return Kind.Data;
    }

    private static bool IsControl(string header)
    {
        var first = header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return Control.Contains(first) || first.StartsWith("if(", StringComparison.Ordinal)
            || first.StartsWith("for(", StringComparison.Ordinal)
            || first.StartsWith("switch(", StringComparison.Ordinal);
    }

    private static int Count(string text, int[] starts, int from, int to, IReadOnlyList<(int From, int To)> excluded)
    {
        var count = 0;
        for (var line = 0; line < starts.Length; line++)
        {
            var start = Math.Max(from, starts[line]);
            var end = Math.Min(to, line + 1 < starts.Length ? starts[line + 1] : text.Length);
            for (var index = start; index < end; index++)
            {
                if (!char.IsWhiteSpace(text[index])
                    && !excluded.Any(span => index >= span.From && index < span.To))
                {
                    count++;
                    break;
                }
            }
        }

        return count;
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

    private static int LineOf(int[] starts, int index)
    {
        var found = Array.BinarySearch(starts, index);
        return found >= 0 ? found + 1 : ~found;
    }

    [GeneratedRegex(@"\bfunc\s*(?:(?:\([^)]*\)\s*)?(?<name>[A-Za-z_]\w*)\s*)?\([^;{}]*\)(?:\s*\([^;{}]*\)|\s*[\w*\[\]., ]+)?$")]
    private static partial Regex GoFunction();

    [GeneratedRegex(@"\b(?:class|struct|interface|record|enum)\s+[A-Za-z_]\w*")]
    private static partial Regex CSharpType();

    [GeneratedRegex(@"(?<name>@?[A-Za-z_]\w*)\s*(?:<[^{};]*>)?\s*\([^(){};]*\)\s*(?:where\s+[^{};]+)?$")]
    private static partial Regex CSharpMethod();

    private enum Kind { Block, Type, Function, Data }

    private sealed class Node(int start, int open, Kind kind, string name)
    {
        public int Start { get; } = start;
        public int Open { get; } = open;
        public int End { get; set; }
        public Kind Kind { get; } = kind;
        public string Name { get; } = name;
        public Node? Parent { get; set; }
        public List<Node> Children { get; } = [];

        public IEnumerable<Node> Ancestors()
        {
            for (var parent = Parent; parent is not null; parent = parent.Parent)
            {
                yield return parent;
            }
        }
    }
}
