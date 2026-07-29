using System.Text.Json;
using FFXIVSpanishPatcher.App.Services;
using Xunit;

namespace FFXIVSpanishPatcher.App.Tests;

public sealed class DalamudPenumbraServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ffxivsp-dalamud-" + Guid.NewGuid().ToString("N"));

    public DalamudPenumbraServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void InspectRoot_RequiresBothDalamudConfigAndRealPenumbraManifest()
    {
        File.WriteAllText(Path.Combine(_root, "dalamudConfig.json"), """{"IsResumeGameAfterPluginLoad":false}""");
        Directory.CreateDirectory(Path.Combine(_root, "installedPlugins", "Penumbra", "1.0.0"));

        var result = new DalamudPenumbraService().InspectRoot(_root);

        Assert.Equal(DalamudPenumbraState.NotDetected, result.State);
    }

    [Fact]
    public void InspectRoot_FindsFalseOption()
    {
        WriteEnvironment(enabled: false);

        var result = new DalamudPenumbraService().InspectRoot(_root);

        Assert.Equal(DalamudPenumbraState.RequiresResumeAfterPluginLoad, result.State);
        Assert.Equal(Path.Combine(_root, "dalamudConfig.json"), result.ConfigPath);
    }

    [Theory]
    [InlineData("dalamudConfig.json")]
    [InlineData("dalamudconfig.json")]
    public void InspectRoot_RecognizesReadyOptionWithKnownConfigCasing(string fileName)
    {
        WriteManifest();
        File.WriteAllText(Path.Combine(_root, fileName), """{"IsResumeGameAfterPluginLoad":true}""");

        var result = new DalamudPenumbraService().InspectRoot(_root);

        Assert.Equal(DalamudPenumbraState.Ready, result.State);
        Assert.Equal(Path.Combine(_root, fileName), result.ConfigPath);
    }

    [Fact]
    public void TryEnable_AtomicallyChangesOnlyRequestedSemanticProperty()
    {
        WriteEnvironment(enabled: false);
        var service = new DalamudPenumbraService();
        var check = service.InspectRoot(_root);

        var changed = service.TryEnableResumeAfterPluginLoad(check);

        Assert.True(changed);
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(_root, "dalamudConfig.json")));
        Assert.True(document.RootElement.GetProperty("IsResumeGameAfterPluginLoad").GetBoolean());
        Assert.Equal("keep", document.RootElement.GetProperty("Unrelated").GetString());
        Assert.Empty(Directory.EnumerateFiles(_root, ".*.tmp"));
    }

    [Fact]
    public void InspectRoot_InvalidJsonFailsSilentlyAsNotDetected()
    {
        WriteManifest();
        File.WriteAllText(Path.Combine(_root, "dalamudConfig.json"), "{broken");

        var result = new DalamudPenumbraService().InspectRoot(_root);

        Assert.Equal(DalamudPenumbraState.NotDetected, result.State);
    }

    private void WriteEnvironment(bool enabled)
    {
        WriteManifest();
        File.WriteAllText(
            Path.Combine(_root, "dalamudConfig.json"),
            $$"""{"IsResumeGameAfterPluginLoad":{{enabled.ToString().ToLowerInvariant()}},"Unrelated":"keep"}""");
    }

    private void WriteManifest()
    {
        var version = Path.Combine(_root, "installedPlugins", "Penumbra", "1.0.0");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "Penumbra.json"), """{"InternalName":"Penumbra"}""");
    }
}
