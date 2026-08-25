namespace Harness.Languages.CSharp;

/// <summary>Counts top-level parameters without treating nested groups or generic arguments as separators.</summary>
internal static class CSharpParameterList
{
    public static int Count(string text, int open)
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
            if (ClosesParameterList(character, ref depth))
            {
                return text[(open + 1)..index].Trim().Length == 0 ? 0 : parameters;
            }

            depth += OpensGroup(character) ? 1 : 0;
            angle += character == '<' ? 1 : 0;
            angle -= ClosesGenericArguments(text, index, angle) ? 1 : 0;
            parameters += IsSeparator(character, depth, angle) ? 1 : 0;
        }

        return -1;
    }

    private static bool ClosesParameterList(char character, ref int depth)
    {
        if (character is not (')' or ']' or '}'))
        {
            return false;
        }

        if (depth == 0)
        {
            return true;
        }

        depth--;
        return false;
    }

    private static bool OpensGroup(char character) => character is '(' or '[' or '{';

    private static bool ClosesGenericArguments(string text, int index, int angle)
        => text[index] == '>' && angle > 0 && text[index - 1] is not ('=' or '-');

    private static bool IsSeparator(char character, int depth, int angle)
        => character == ',' && depth == 0 && angle == 0;
}
