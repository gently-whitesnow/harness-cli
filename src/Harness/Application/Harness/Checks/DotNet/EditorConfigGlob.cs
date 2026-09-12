using System.Text;
using System.Text.RegularExpressions;

namespace Harness.Checks.DotNet;

/// <summary>
/// The section-header glob of an `.editorconfig`, matched the way the specification reads
/// it: a pattern without a slash addresses a file name at any depth, one with a slash is
/// anchored at the directory of the file; `*` stops at a slash, `**` does not, `{a,b}`
/// lists alternatives.
/// </summary>
internal static class EditorConfigGlob
{
    public static bool Matches(string glob, string relativePath)
    {
        var pattern = glob.Trim();
        if (pattern.Length == 0)
        {
            return false;
        }

        var anchored = pattern.Contains('/');
        if (pattern.StartsWith('/'))
        {
            pattern = pattern[1..];
        }

        var regex = new StringBuilder(anchored ? "^" : "(^|/)");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            switch (character)
            {
                // A whole **/ segment also matches zero directories.
                case '*' when index + 2 < pattern.Length && pattern[index + 1] == '*'
                    && pattern[index + 2] == '/' && (index == 0 || pattern[index - 1] == '/'):
                    regex.Append("(?:.*/)?");
                    index += 2;
                    break;
                case '*' when index + 1 < pattern.Length && pattern[index + 1] == '*':
                    regex.Append(".*");
                    index++;
                    break;
                case '*':
                    regex.Append("[^/]*");
                    break;
                case '?':
                    regex.Append("[^/]");
                    break;
                case '{':
                    regex.Append("(?:");
                    break;
                case ',':
                    regex.Append('|');
                    break;
                case '}':
                    regex.Append(')');
                    break;
                // EditorConfig negates a character class with !, while regex uses ^.
                case '[' when index + 1 < pattern.Length && pattern[index + 1] == '!':
                    regex.Append("[^");
                    index++;
                    break;
                case '[':
                case ']':
                    regex.Append(character);
                    break;
                default:
                    regex.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        regex.Append('$');
        return Regex.IsMatch(relativePath, regex.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
