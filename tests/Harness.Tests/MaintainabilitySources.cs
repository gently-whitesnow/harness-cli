using System.Text;

namespace Harness.Tests;

/// <summary>C# shaped to exceed exactly one maintainability comparison point at a time.</summary>
internal static class MaintainabilitySources
{
    public static string LongMethod(int statements)
        => $$"""
        namespace App;

        public static class Report
        {
            public static int Compute(int seed)
            {
                var total = seed;
        {{Statements(statements)}}        return total;
            }
        }

        """;

    public static string LongTupleReturningMethod(int statements)
        => $$"""
        namespace App;

        public static class Report
        {
            public static (int A, int B, int C, int D, int E, int F, int G) Compute(int seed)
            {
                var total = seed;
        {{Statements(statements)}}        return (total, 0, 0, 0, 0, 0, 0);
            }
        }

        """;

    public static string LongMethodWithSplitSignature(int statements)
        => $$"""
        namespace App;

        public static class Report
        {
            public static int Compute(
                int seed,
                int offset)
            {
                var total = seed + offset;
        {{Statements(statements)}}        return total;
            }
        }

        """;

    public static string LongMethodAfterAwkwardLiterals(int statements)
        => $$$$""""
        namespace App;

        public static class Report
        {
            public static string Interpolate(int a, int b)
            {
                var hole = $"{(a > b ? "left" : "right")} and {a switch { 0 => "zero", _ => "other" }}";
                var nested = $"outer {Echo($"inner {a}")} end";
                var escaped = $"{{literal braces}} and {a}";
                var commented = $"{/* } " */ a}";
                var verbatim = $@"{(a > b ? "x" : "y")} "" tail";
                var raw = $$"""
                    {{a}} a literal { and a literal } and a " quote
                    """;
                return hole + nested + escaped + commented + verbatim + raw;
            }

            private static string Echo(string value) => value;

            public static int Compute(int seed)
            {
                var total = seed;
        {{{{Statements(statements)}}}}        return total;
            }
        }

        """";

    public static string NestedTypeWithLongMethod(int statements)
        => $$"""
        namespace App;

        public static class Outer
        {
            public static class Inner
            {
                public static int Compute(int seed)
                {
                    var total = seed;
        {{Statements(statements)}}            return total;
                }
            }
        }

        """;

    public const string WideRecord =
        """
    namespace App;

    public sealed record Money(
        decimal Amount,
        string Currency,
        string Region,
        string Ledger,
        string Owner,
        string Category,
        string Note);

    """;

    public const string WidePrimaryConstructor =
        """
    namespace App;

    public sealed class Engine(
        string a,
        string b,
        string c,
        string d,
        string e,
        string f,
        string g)
    {
        public string Name => a + b + c + d + e + f + g;
    }

    """;

    public const string WideConstructor =
        """
    namespace App;

    public sealed class Service
    {
        public Service(
            string a,
            string b,
            string c,
            string d,
            string e,
            string f,
            string g)
        {
            Name = a + b + c + d + e + f + g;
        }

        public string Name { get; }
    }

    """;

    public const string ExpressionBodiedMembers =
        """
    namespace App;

    public sealed record Point(int X, int Y)
    {
        public int Sum => X + Y;

        public int Scaled(int factor) => Sum * factor;
    }

    public static class Compact
    {
        private static readonly System.Func<int, int> Double = value => value * 2;

        public static int Twice(int value) => Double(value);

        public static string Describe(int value) => value switch
        {
            0 => "zero",
            _ => "other",
        };
    }

    """;


    public static string WidePublicSurface(int members)
    {
        var text = new StringBuilder("namespace App;\n\npublic sealed class Facade\n{\n");
        for (var index = 0; index < members; index++)
        {
            text.Append("    public int Member").Append(index).Append("() => ").Append(index).Append(";\n");
        }

        return text.Append("}\n").ToString();
    }


    /// <summary>One type long enough to exceed both the file and the type comparison point.</summary>
    public static string LargeType(int members)
    {
        var text = new StringBuilder("namespace App;\n\npublic static class Big\n{\n");
        for (var index = 0; index < members; index++)
        {
            text.Append("    internal static int Value").Append(index).Append(" => ").Append(index).Append(";\n");
        }

        return text.Append("}\n").ToString();
    }

    private static string Statements(int count)
    {
        var text = new StringBuilder();
        for (var index = 0; index < count; index++)
        {
            text.Append("        total += ").Append(index).Append(";\n");
        }

        return text.ToString();
    }
}
