using System.Reflection;
using System.Runtime.ExceptionServices;
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
        // Status, sheet, field and page path repeat across hundreds of thousands of rows, yet the
        // deserializer allocates a fresh string each time. Sharing one instance per distinct value
        // saves about 125 MB on the shipped corpus. A local pool, not string.Intern: the plugin
        // runs inside the game process, where an interned string would never be released.
        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        string? Shared(string? value)
        {
            if (value is null)
            {
                return null;
            }

            if (!pool.TryGetValue(value, out var shared))
            {
                pool[value] = shared = value;
            }

            return shared;
        }

        // Decompression and line splitting are inherently serial; parsing each line is not, and it
        // is most of the load time. AsOrdered keeps the corpus order the pipeline relies on.
        IEnumerable<string> Lines()
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    yield return line;
                }
            }
        }

        try
        {
            // One core is left alone: this also runs inside the game, where the plugin loads it.
            var parsed = Lines()
                .AsParallel()
                .AsOrdered()
                .WithDegreeOfParallelism(Math.Max(1, Environment.ProcessorCount - 1))
                .Select(JsonlSerialization.Deserialize);
            foreach (var entry in parsed)
            {
                if (entry is not null)
                {
                    entry.Status = Shared(entry.Status);
                    if (entry.SourceKey is { } key)
                    {
                        key.Sheet = Shared(key.Sheet)!;
                        key.Field = Shared(key.Field)!;
                        key.ExdPath = Shared(key.ExdPath);
                    }

                    entries.Add(entry);
                }
            }
        }
        catch (AggregateException aggregate)
        {
            // Callers tell a damaged blob apart by exception type; hand them the real failure.
            ExceptionDispatchInfo.Capture(aggregate.Flatten().InnerExceptions[0]).Throw();
        }

        return entries;
    }
}
