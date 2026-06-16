using System.Security.Cryptography;
using System.Text;

namespace XivSpanish.Translation;

public static class TextHasher
{
    public static string Hash(string? text)
    {
        var normalized = TextNormalizer.Normalize(text);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}

