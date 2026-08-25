using System.Text;

namespace Harness.Tests;

/// <summary>C# written so that two files repeat a block the normalizer can recognize.</summary>
internal static class DuplicationSources
{
    public static string Block(string type, string field, string literal)
        => $$"""
        namespace App;

        public static class {{type}}
        {
            public static int Compute(int {{field}})
            {
                var total = 0;
                foreach (var item in Items)
                {
                    if (item > {{field}})
                    {
                        total += item * 2;
                        continue;
                    }

                    if (item < 0)
                    {
                        total -= item;
                        continue;
                    }

                    total += item + {{field}};
                }

                Log("{{literal}}");
                return total;
            }

            private static int[] Items => [1, 2, 3];

            private static void Log(string message)
            {
            }
        }

        """;

    public static string TruncatedBlock(string type, string field)
        => $$"""
        namespace App;

        public static class {{type}}
        {
            public static int Compute(int {{field}})
            {
                var total = 0;
                foreach (var item in Items)
                {
                    if (item > {{field}})
                    {
                        total += item * 2;
                        continue;
                    }

                    if (item < 0)
                    {
                        total -= item;
                        continue;
                    }

                    total += item + {{field}};
                }

                Log("truncated");
                return total;
            }

            private static int[] Items => [1, 2, 3];

            private static void Log(string message)
            {
            }
        }

        """;

    public const string SameBlockTwiceInOneFile =
        """
    namespace App;

    public static class Twice
    {
        public static int First(int seed)
        {
            var total = 0;
            foreach (var item in Items)
            {
                if (item > seed)
                {
                    total += item * 2;
                    continue;
                }

                total += item + seed;
            }

            return total;
        }

        public static int Second(int start)
        {
            var total = 0;
            foreach (var item in Items)
            {
                if (item > start)
                {
                    total += item * 2;
                    continue;
                }

                total += item + start;
            }

            return total;
        }

        private static int[] Items => [1, 2, 3];
    }

    """;

    public const string BlockQuotedInARawString =
        """"
    namespace App;

    public sealed record Snippet(string Text)
    {
        public static readonly Snippet Sample = new("""
            public static int Compute(int seed)
            {
                var total = 0;
                foreach (var item in Items)
                {
                    if (item > seed)
                    {
                        total += item * 2;
                        continue;
                    }

                    if (item < 0)
                    {
                        total -= item;
                        continue;
                    }

                    total += item + seed;
                }

                Log("quoted");
                return total;
            }
            """);
    }

    """";

    public static string AwkwardLiteralsThenBlock(string type, string field, string literal)
        => Block(type, field, literal).Replace(
            "    public static int Compute(",
            $$$$""""
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

            public static int Compute(
        """",
            StringComparison.Ordinal);

    public static string AwkwardCharactersThenBlock(string type, string field, string literal)
        => Block(type, field, literal).Replace(
            "    public static int Compute(",
            """
            private static readonly char[] Delimiters =
            [
                '"', '\'', '{', '}', '\\', '/', '#', '$', '@',
            ];

            public static int Compute(
        """,
            StringComparison.Ordinal);

    public static string PropertyBag(string type, string owner)
    {
        var text = new StringBuilder($"namespace App;\n\npublic sealed class {type}\n{{\n");
        foreach (var member in new[]
        {
            "Reference", "Opened", "Closed", "Note", "Category", "State", "Priority", "Region",
            "Owner", "Team", "Channel", "Sequence", "Label", "Description", "Kind",
        })
        {
            text.Append("    public string ").Append(member).Append(" { get; init; } = string.Empty;\n\n");
        }

        return text.Append("    public string ").Append(owner).Append(" { get; init; } = string.Empty;\n}\n")
            .ToString();
    }

    public static string SmallRecord(string type, string first, string second)
        => $$"""
        namespace App;

        public sealed record {{type}}(int {{first}}, int {{second}});

        """;

    public static string DistinctBlock(int index, string side)
    {
        var call = "Sum(" + string.Join(", ", Enumerable.Repeat("seed", index + 1)) + ")";
        var body = new StringBuilder();
        for (var statement = 0; statement < 10; statement++)
        {
            body.Append("        total += ").Append(call).Append(";\n");
        }

        return $$"""
        namespace App;

        public static class Block{{side}}{{index}}
        {
            public static int Compute(int seed)
            {
                var total = seed;
        {{body}}        return total;
            }

            private static int Sum(params int[] values) => values.Length;
        }

        """;
    }
}
