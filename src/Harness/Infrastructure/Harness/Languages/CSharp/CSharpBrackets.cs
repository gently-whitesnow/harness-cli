namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>Shared lexical navigation for square-bracketed C# regions.</summary>
internal static class CSharpBrackets
{
    public static int CloseOf(string text, int open)
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
