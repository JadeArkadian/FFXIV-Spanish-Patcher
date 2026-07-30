using System.Reflection;
using System.Text;
using XivSpanish.Translation;
using ZstdSharp;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Loads translation entries from a Zstandard-compressed JSONL blob — the <c>translations.dat</c> the
/// app embeds as a resource (built by the <c>tools/XivSpanish.BlobBuilder</c> tool). The blob is opened lazily
/// so the source can wrap an embedded resource, a file, or an in-memory buffer (tests).
/// </summary>
public sealed class EmbeddedTranslationSource(Func<Stream> openCompressedBlob) : ITranslationSource
{
    private readonly Func<Stream> _open = openCompressedBlob;

    /// <summary>Wraps a Zstandard-JSONL resource embedded in <paramref name="assembly"/>.</summary>
    public static EmbeddedTranslationSource FromAssemblyResource(Assembly assembly, string resourceName)
        => new(() => assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded translation resource not found: {resourceName}"));

    /// <summary>Wraps a Zstandard-JSONL blob stored beside a portable application publish.</summary>
    public static EmbeddedTranslationSource FromFile(string path)
        => new(() => File.OpenRead(path));

    public IReadOnlyList<TranslationEntry> Load()
    {
        using var compressed = _open();
        using var zstandard = new DecompressionStream(compressed);
        using var reader = new StreamReader(zstandard, Encoding.UTF8);

        var entries = new List<TranslationEntry>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonlSerialization.Deserialize(line);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }
}
