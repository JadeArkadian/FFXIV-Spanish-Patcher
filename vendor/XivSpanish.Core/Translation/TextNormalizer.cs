using System.Text.RegularExpressions;

namespace XivSpanish.Translation;

public static partial class TextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return AnyWhitespace().Replace(text.Trim(), " ");
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex AnyWhitespace();
}
