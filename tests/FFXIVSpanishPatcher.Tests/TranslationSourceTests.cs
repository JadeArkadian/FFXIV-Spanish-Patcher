using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.Translation;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class TranslationSourceTests : IDisposable
{
    private readonly string _temp;

    public TranslationSourceTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "ffxivsp-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    [Fact]
    public void ManifestFileTranslationSource_LoadsJsonl()
    {
        var entries = new[]
        {
            new TranslationEntry { Source = "Hello", Target = "Hola", Status = TranslationEntryStatus.Approved },
            new TranslationEntry { Source = "World", Target = "Mundo", Status = TranslationEntryStatus.Approved },
        };
        var path = Path.Combine(_temp, "manifest.jsonl");
        File.WriteAllLines(path, entries.Select(e => JsonSerializer.Serialize(e)));

        var loaded = new ManifestFileTranslationSource(path).Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Hola", loaded[0].Target);
        Assert.Equal("Mundo", loaded[1].Target);
    }

    [Fact]
    public void ListTranslationSource_ReturnsTheSameEntries()
    {
        var entries = new List<TranslationEntry> { new() { Source = "only" } };

        var loaded = new ListTranslationSource(entries).Load();

        Assert.Single(loaded);
        Assert.Equal("only", loaded[0].Source);
    }

    [Fact]
    public void EmbeddedTranslationSource_LoadsBrotliJsonl()
    {
        // Mirrors the XivSpanish.BlobBuilder blob format: Brotli of newline-delimited TranslationEntry JSON.
        var jsonl = string.Join('\n',
            JsonSerializer.Serialize(new TranslationEntry { Source = "A", Target = "a", Status = TranslationEntryStatus.Approved }),
            JsonSerializer.Serialize(new TranslationEntry { Source = "B", Target = "b", Status = TranslationEntryStatus.Approved }));

        using var buffer = new MemoryStream();
        using (var brotli = new BrotliStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(brotli, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(jsonl);
        }

        var blob = buffer.ToArray();
        var loaded = new EmbeddedTranslationSource(() => new MemoryStream(blob)).Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("a", loaded[0].Target);
        Assert.Equal("b", loaded[1].Target);
    }

    [Fact]
    public void EmbeddedTranslationSource_FromFileLoadsBrotliJsonl()
    {
        var path = Path.Combine(_temp, "translations.dat");
        using (var output = File.Create(path))
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal))
        using (var writer = new StreamWriter(brotli, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(JsonSerializer.Serialize(new TranslationEntry
            {
                Source = "External",
                Target = "Lateral",
                Status = TranslationEntryStatus.Approved,
            }));
        }

        var loaded = EmbeddedTranslationSource.FromFile(path).Load();

        var entry = Assert.Single(loaded);
        Assert.Equal("Lateral", entry.Target);
    }
}
