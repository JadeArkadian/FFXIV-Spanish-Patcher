using System.Text;
using System.Text.Json;
using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.Translation;
using Xunit;
using ZstdSharp;

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
    public void ListTranslationSource_ReturnsTheSameEntries()
    {
        var entries = new List<TranslationEntry> { new() { Source = "only" } };

        var loaded = new ListTranslationSource(entries).Load();

        Assert.Single(loaded);
        Assert.Equal("only", loaded[0].Source);
    }

    [Fact]
    public void EmbeddedTranslationSource_LoadsZstandardJsonl()
    {
        // Mirrors the XivSpanish.BlobBuilder blob format: Zstandard of newline-delimited TranslationEntry JSON.
        var jsonl = string.Join('\n',
            JsonSerializer.Serialize(new TranslationEntry { Source = "A", Target = "a", Status = TranslationEntryStatus.Approved }),
            JsonSerializer.Serialize(new TranslationEntry { Source = "B", Target = "b", Status = TranslationEntryStatus.Approved }));

        using var compressor = new Compressor();
        var blob = compressor.Wrap(Encoding.UTF8.GetBytes(jsonl)).ToArray();
        var loaded = new EmbeddedTranslationSource(() => new MemoryStream(blob)).Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("a", loaded[0].Target);
        Assert.Equal("b", loaded[1].Target);
    }

    [Fact]
    public void EmbeddedTranslationSource_SharesRepeatedKeyStrings()
    {
        static string Row(uint rowId) => JsonSerializer.Serialize(new TranslationEntry
        {
            Source = $"Source {rowId}",
            Target = $"Destino {rowId}",
            Status = TranslationEntryStatus.Approved,
            SourceKey = new TranslationSourceKey { Sheet = "Addon", RowId = rowId, Field = "Text", ExdPath = "exd/addon_0_en.exd" },
        });

        using var compressor = new Compressor();
        var blob = compressor.Wrap(Encoding.UTF8.GetBytes(Row(1) + "\n" + Row(2))).ToArray();
        var loaded = new EmbeddedTranslationSource(() => new MemoryStream(blob)).Load();

        Assert.Equal(2, loaded.Count);
        Assert.Same(loaded[0].Status, loaded[1].Status);
        Assert.Same(loaded[0].SourceKey!.Sheet, loaded[1].SourceKey!.Sheet);
        Assert.Same(loaded[0].SourceKey!.Field, loaded[1].SourceKey!.Field);
        Assert.Same(loaded[0].SourceKey!.ExdPath, loaded[1].SourceKey!.ExdPath);
        Assert.Equal("Destino 2", loaded[1].Target);
    }

    [Fact]
    public void EmbeddedTranslationSource_FromFileLoadsZstandardJsonl()
    {
        var path = Path.Combine(_temp, "translations.dat");
        using (var compressor = new Compressor())
        {
            var json = JsonSerializer.Serialize(new TranslationEntry
            {
                Source = "External",
                Target = "Lateral",
                Status = TranslationEntryStatus.Approved,
            });
            File.WriteAllBytes(path, compressor.Wrap(Encoding.UTF8.GetBytes(json)).ToArray());
        }

        var loaded = EmbeddedTranslationSource.FromFile(path).Load();

        var entry = Assert.Single(loaded);
        Assert.Equal("Lateral", entry.Target);
    }
}
