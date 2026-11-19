using System.Text.Json;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PenumbraModIdentityServiceTests : IDisposable
{
    private readonly string _modRoot = Path.Combine(
        Path.GetTempPath(),
        "ffxivspanish-identity-" + Guid.NewGuid().ToString("N"));

    public PenumbraModIdentityServiceTests() => Directory.CreateDirectory(_modRoot);

    public void Dispose()
    {
        if (Directory.Exists(_modRoot))
        {
            Directory.Delete(_modRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{"ModTags":[1,{"a":2},null,["x"]]}""")]
    [InlineData("""{"FileVersion":"4","ModTags":"ffxiv-spanish-managed"}""")]
    [InlineData("""{"FileVersion":4,"Groups":[{"Name":7,"Type":true,"Options":[{"Name":{},"Files":{}}]}]}""")]
    [InlineData("""{"FileVersion":4,"Groups":[{"Name":"Categorías de traducción","Type":"Multi","Options":[{"Name":3,"Files":{}}]}]}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""not json at all""")]
    public void ForeignModWithUnexpectedMetadata_IsIgnoredAndDoesNotBreakTheScan(string foreignMeta)
    {
        WriteMod("SomeoneElse", foreignMeta);
        WriteManagedMod("FFXIVSpanish");
        var registry = Registry("SomeoneElse", "FFXIVSpanish");

        var resolution = new PenumbraModIdentityService(registry).Resolve(_modRoot);

        Assert.Equal(ModIdentityResolutionKind.AdoptExisting, resolution.Kind);
        Assert.Equal("FFXIVSpanish", resolution.Selected!.DirectoryName);
    }

    [Fact]
    public void CachedResolver_ReusesTheAnswerWhileTheModSetIsUnchanged()
    {
        WriteManagedMod("FFXIVSpanish");
        var registry = Registry("FFXIVSpanish");
        var resolver = new CachedModIdentityResolver(registry);
        Assert.Equal(ModIdentityResolutionKind.AdoptExisting, resolver.Resolve(_modRoot).Kind);

        // Proof that the second call reads no metadata: the file on disk no longer identifies the
        // mod, yet the remembered answer stands until something invalidates it.
        WriteMod("FFXIVSpanish", "{}");

        Assert.Equal(ModIdentityResolutionKind.AdoptExisting, resolver.Resolve(_modRoot).Kind);
        resolver.Invalidate();
        Assert.Equal(ModIdentityResolutionKind.NewInstallation, resolver.Resolve(_modRoot).Kind);
    }

    [Fact]
    public void CachedResolver_RescansWhenPenumbraReportsADifferentModSet()
    {
        var registry = Registry("Other");
        WriteMod("Other", "{}");
        var resolver = new CachedModIdentityResolver(registry);
        Assert.Equal(ModIdentityResolutionKind.NewInstallation, resolver.Resolve(_modRoot).Kind);

        WriteManagedMod("FFXIVSpanish");
        registry.Mods["FFXIVSpanish"] = "FFXIV en Español";

        Assert.Equal(ModIdentityResolutionKind.AdoptExisting, resolver.Resolve(_modRoot).Kind);
    }

    [Fact]
    public void CachedResolver_RescansWhenTheRememberedModLeavesTheDisk()
    {
        WriteManagedMod("FFXIVSpanish");
        var registry = Registry("FFXIVSpanish");
        var resolver = new CachedModIdentityResolver(registry);
        Assert.Equal(ModIdentityResolutionKind.AdoptExisting, resolver.Resolve(_modRoot).Kind);

        Directory.Delete(Path.Combine(_modRoot, "FFXIVSpanish"), recursive: true);

        Assert.Equal(ModIdentityResolutionKind.NewInstallation, resolver.Resolve(_modRoot).Kind);
    }

    private void WriteManagedMod(string directory)
        => WriteMod(directory, JsonSerializer.Serialize(new
        {
            FileVersion = 4,
            Name = PenumbraModIdentity.LogicalName,
            ModTags = new[] { PenumbraModIdentity.ManagedTag },
        }));

    private void WriteMod(string directory, string metaJson)
    {
        var path = Path.Combine(_modRoot, directory);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "meta.json"), metaJson);
    }

    private static FakeRegistry Registry(params string[] directories)
        => new() { Mods = directories.ToDictionary(name => name, name => name, StringComparer.Ordinal) };

    private sealed class FakeRegistry : IPenumbraModRegistry
    {
        public Dictionary<string, string> Mods { get; init; } = [];

        public IReadOnlyDictionary<string, string> GetModList() => Mods;

        public int AddMod(string directoryName) => 0;

        public int ReloadMod(string directoryName) => 0;

        public int SetEnabled(string directoryName, bool enabled) => 0;
    }
}
