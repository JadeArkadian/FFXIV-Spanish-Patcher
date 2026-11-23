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
        Assert.True(File.Exists(result.ConfigPath));
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(Path.GetDirectoryName(result.ConfigPath)!));
    }

    [Fact]
    public void TryEnable_LeavesTheFileUntouchedWhileTheGameIsRunning()
    {
        WriteEnvironment(enabled: false);
        var configPath = Path.Combine(_root, "dalamudConfig.json");
        var before = File.ReadAllBytes(configPath);
        var service = new DalamudPenumbraService(isGameRunning: () => true);

        var changed = service.TryEnableResumeAfterPluginLoad(service.InspectRoot(_root));

        Assert.False(changed);
        Assert.Equal(before, File.ReadAllBytes(configPath));
        Assert.Empty(Directory.EnumerateFiles(_root, ".*.tmp"));
    }

    [Fact]
    public void TryEnable_AtomicallyChangesOnlyRequestedSemanticProperty()
    {
        WriteEnvironment(enabled: false);
        // The developer's own game may be open while the suite runs.
        var service = new DalamudPenumbraService(isGameRunning: () => false);
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

    [Fact]
    public void AddPluginRepository_AppendsToDalamudTypedListAndKeepsTheRest()
    {
        WriteConfig(
            """{"$type":"Dalamud.Configuration.Internal.DalamudConfiguration, Dalamud","Unrelated":"keep",""" +
            RepositoryList("https://example.invalid/other.json", enabled: true) + "}");
        var service = new DalamudPenumbraService(isGameRunning: () => false, root: _root);
        Assert.Equal(PluginRepositoryState.Missing, service.InspectPluginRepository(RepositoryUrl));

        Assert.True(service.TryAddPluginRepository(RepositoryUrl));

        Assert.Equal(PluginRepositoryState.Present, service.InspectPluginRepository(RepositoryUrl));
        using var document = ReadConfig();
        Assert.Equal("keep", document.RootElement.GetProperty("Unrelated").GetString());
        var list = document.RootElement.GetProperty("ThirdRepoList");
        // Newtonsoft only honours type names that come first.
        Assert.Equal("$type", list.EnumerateObject().First().Name);
        var entries = list.GetProperty("$values").EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal("https://example.invalid/other.json", entries[0].GetProperty("Url").GetString());
        Assert.Equal("$type", entries[1].EnumerateObject().First().Name);
        Assert.Equal(EntryType, entries[1].GetProperty("$type").GetString());
        Assert.Equal(RepositoryUrl, entries[1].GetProperty("Url").GetString());
        Assert.True(entries[1].GetProperty("IsEnabled").GetBoolean());
        Assert.Empty(Directory.EnumerateFiles(_root, ".*.tmp"));
    }

    [Fact]
    public void AddPluginRepository_CreatesTheListWhenDalamudHasNone()
    {
        WriteConfig("""{"Unrelated":"keep"}""");
        var service = new DalamudPenumbraService(isGameRunning: () => false, root: _root);

        Assert.True(service.TryAddPluginRepository(RepositoryUrl));

        using var document = ReadConfig();
        var list = document.RootElement.GetProperty("ThirdRepoList");
        Assert.Equal(ListType, list.GetProperty("$type").GetString());
        Assert.Equal(RepositoryUrl, list.GetProperty("$values")[0].GetProperty("Url").GetString());
    }

    [Fact]
    public void AddPluginRepository_ReenablesAnExistingEntryInsteadOfDuplicatingIt()
    {
        WriteConfig("{" + RepositoryList(RepositoryUrl, enabled: false) + "}");
        var service = new DalamudPenumbraService(isGameRunning: () => false, root: _root);
        Assert.Equal(PluginRepositoryState.Missing, service.InspectPluginRepository(RepositoryUrl));

        Assert.True(service.TryAddPluginRepository(RepositoryUrl));

        using var document = ReadConfig();
        var entry = Assert.Single(document.RootElement.GetProperty("ThirdRepoList").GetProperty("$values").EnumerateArray());
        Assert.True(entry.GetProperty("IsEnabled").GetBoolean());
    }

    [Fact]
    public void AddPluginRepository_DoesNotReuseAnEntrySpelledDifferently()
    {
        // Dalamud pairs plugin and repository by exact URL: reusing this entry would orphan the plugin.
        WriteConfig("{" + RepositoryList(RepositoryUrl.ToUpperInvariant(), enabled: false) + "}");
        var service = new DalamudPenumbraService(isGameRunning: () => false, root: _root);

        Assert.True(service.TryAddPluginRepository(RepositoryUrl));

        using var document = ReadConfig();
        var urls = document.RootElement.GetProperty("ThirdRepoList").GetProperty("$values")
            .EnumerateArray().Select(entry => entry.GetProperty("Url").GetString());
        Assert.Equal([RepositoryUrl.ToUpperInvariant(), RepositoryUrl], urls);
    }

    [Theory]
    [InlineData(true, """{"Unrelated":"keep"}""")]
    [InlineData(false, """{"ThirdRepoList":"unknown shape"}""")]
    public void AddPluginRepository_LeavesTheFileUntouchedWhenItCannotBeDoneSafely(bool gameRunning, string config)
    {
        WriteConfig(config);
        var before = File.ReadAllBytes(Path.Combine(_root, "dalamudConfig.json"));
        var service = new DalamudPenumbraService(isGameRunning: () => gameRunning, root: _root);

        Assert.False(service.TryAddPluginRepository(RepositoryUrl));

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_root, "dalamudConfig.json")));
        Assert.Empty(Directory.EnumerateFiles(_root, ".*.tmp"));
    }

    [Fact]
    public void PluginRepository_WithoutDalamudConfigIsNotDetectedAndNothingIsCreated()
    {
        var service = new DalamudPenumbraService(isGameRunning: () => false, root: _root);

        Assert.Equal(PluginRepositoryState.DalamudNotDetected, service.InspectPluginRepository(RepositoryUrl));
        Assert.False(service.TryAddPluginRepository(RepositoryUrl));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("ready", true)]
    [InlineData("no dalamud", false)]
    [InlineData("game running", false)]
    [InlineData("config locked", false)]
    [InlineData("repository present", false)]
    [InlineData("plugin installed", false)]
    public void CanInstallPlugin_OnlyWhenThePatcherCanCarryItOut(string scenario, bool expected)
    {
        var configPath = Path.Combine(_root, "dalamudConfig.json");
        if (scenario != "no dalamud")
        {
            WriteConfig(scenario == "repository present"
                ? "{" + RepositoryList(RepositoryUrl, enabled: true) + "}"
                : "{}");
        }

        if (scenario == "plugin installed")
        {
            var setup = new DalamudPenumbraService(isGameRunning: () => false, root: _root);
            Assert.True(setup.InstallPlugin(TestPluginPackage.Create(), RepositoryUrl));
        }

        if (scenario == "config locked")
        {
            File.SetAttributes(configPath, FileAttributes.ReadOnly);
        }

        try
        {
            var service = new DalamudPenumbraService(isGameRunning: () => scenario == "game running", root: _root);

            Assert.Equal(expected, service.CanInstallPlugin(RepositoryUrl));
        }
        finally
        {
            if (File.Exists(configPath))
            {
                File.SetAttributes(configPath, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void InstallPlugin_LeavesThePackageWhereDalamudLoadsItFrom()
    {
        WriteConfig("{}");
        var service = new DalamudPenumbraService(isGameRunning: () => false, root: _root);
        Assert.False(service.IsPluginInstalled());

        Assert.True(service.InstallPlugin(TestPluginPackage.Create(), RepositoryUrl));

        var version = Path.Combine(_root, "installedPlugins", TestPluginPackage.Name, "0.6.0.0");
        Assert.True(File.Exists(Path.Combine(version, TestPluginPackage.Name + ".dll")));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(version, TestPluginPackage.Name + ".json")));
        // Without the source repository Dalamud would never offer an update.
        Assert.Equal(RepositoryUrl, manifest.RootElement.GetProperty("InstalledFromUrl").GetString());
        Assert.NotEqual(Guid.Empty, manifest.RootElement.GetProperty("WorkingPluginId").GetGuid());
        Assert.Equal(15, manifest.RootElement.GetProperty("DalamudApiLevel").GetInt32());
        Assert.True(service.IsPluginInstalled());
        Assert.Empty(Directory.EnumerateDirectories(_root, ".*.tmp"));

        // Updating an installed plugin is Dalamud's job.
        Assert.True(service.InstallPlugin(TestPluginPackage.Create(version: "9.9.9.9"), RepositoryUrl));
        Assert.Single(Directory.EnumerateDirectories(Path.Combine(_root, "installedPlugins", TestPluginPackage.Name)));
    }

    [Theory]
    [InlineData("escaping entry")]
    [InlineData("missing dll")]
    [InlineData("another plugin")]
    [InlineData("version is a path")]
    [InlineData("not a zip")]
    [InlineData("game running")]
    public void InstallPlugin_RejectsAnUnsafePackageAndLeavesNothingBehind(string scenario)
    {
        WriteConfig("{}");
        var package = scenario switch
        {
            "escaping entry" => TestPluginPackage.Create(extraEntry: "../../escaped.txt"),
            "missing dll" => TestPluginPackage.Create(withDll: false),
            "another plugin" => TestPluginPackage.Create(internalName: "Penumbra"),
            "version is a path" => TestPluginPackage.Create(version: "../../escaped"),
            "not a zip" => [1, 2, 3],
            _ => TestPluginPackage.Create(),
        };
        var service = new DalamudPenumbraService(isGameRunning: () => scenario == "game running", root: _root);

        Assert.False(service.InstallPlugin(package, RepositoryUrl));

        Assert.Equal(["dalamudConfig.json"], Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escaped.txt")));
        Assert.False(service.IsPluginInstalled());
    }

    private const string RepositoryUrl = "https://example.invalid/dalamud/pluginmaster.json";
    private const string ListType =
        "System.Collections.Generic.List`1[[Dalamud.Configuration.ThirdPartyRepoSettings, Dalamud]], System.Private.CoreLib";
    private const string EntryType = "Dalamud.Configuration.ThirdPartyRepoSettings, Dalamud";

    /// <summary>The list as Dalamud writes it: Newtonsoft type names around a single repository.</summary>
    private static string RepositoryList(string url, bool enabled)
        => $"\"ThirdRepoList\":{{\"$type\":\"{ListType}\",\"$values\":[{{\"$type\":\"{EntryType}\"," +
           $"\"Url\":\"{url}\",\"IsEnabled\":{(enabled ? "true" : "false")}}}]}}";

    private void WriteConfig(string json) => File.WriteAllText(Path.Combine(_root, "dalamudConfig.json"), json);

    private JsonDocument ReadConfig()
        => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "dalamudConfig.json")));

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
