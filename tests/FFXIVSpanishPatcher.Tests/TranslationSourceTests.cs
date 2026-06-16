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
            new TranslationEntry { Id = "x1", Source = "Hello", Target = "Hola", Status = TranslationEntryStatus.Approved },
            new TranslationEntry { Id = "x2", Source = "World", Target = "Mundo", Status = TranslationEntryStatus.Approved },
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
        var entries = new List<TranslationEntry> { new() { Id = "only" } };

        var loaded = new ListTranslationSource(entries).Load();

        Assert.Single(loaded);
        Assert.Equal("only", loaded[0].Id);
    }
}
