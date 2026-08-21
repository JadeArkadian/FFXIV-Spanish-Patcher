using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PluginPackageTests
{
    [Fact]
    public void SharedEngineAssemblies_LoadWithoutPrivateDalamudAssembly()
    {
        Assert.NotNull(Assembly.Load("FFXIVSpanishPatcher.Pipeline"));
        Assert.NotNull(Assembly.Load("XivSpanish.GameData"));
        Assert.NotNull(Assembly.Load("XivSpanish.Packaging"));
    }

    [Fact]
    public void ReleasePackage_HasManifestPluginAndResourcesWithoutDalamudSharedAssemblies()
    {
        var resourceNames = typeof(PluginLoadProbe).Assembly.GetManifestResourceNames();
        Assert.Contains("FFXIVSpanishTranslationManager.translations.dat", resourceNames);
        Assert.Contains("FFXIVSpanishTranslationManager.recommended-game-version.txt", resourceNames);
        Assert.Contains("FFXIVSpanishTranslationManager.translation-milestones.md", resourceNames);

        using var archive = ZipFile.OpenRead(ReleasePackagePath());
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("FFXIVSpanishTranslationManager.dll", names);
        Assert.Contains("FFXIVSpanishTranslationManager.json", names);
        Assert.DoesNotContain(names, name => name is "Dalamud.dll" or "Lumina.dll" or "Lumina.Excel.dll"
            or "Microsoft.Extensions.ObjectPool.dll");

        using var manifest = JsonDocument.Parse(archive.GetEntry("FFXIVSpanishTranslationManager.json")!.Open());
        Assert.Equal("FFXIVSpanishTranslationManager", manifest.RootElement.GetProperty("InternalName").GetString());
        Assert.Equal(15, manifest.RootElement.GetProperty("DalamudApiLevel").GetInt32());
        Assert.True(manifest.RootElement.GetProperty("LoadSync").GetBoolean());
        Assert.True(manifest.RootElement.GetProperty("CanUnloadAsync").GetBoolean());
    }

    private static string ReleasePackagePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FFXIVSpanishPatcher.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var package = Path.Combine(
            directory!.FullName,
            "src",
            "FFXIVSpanishPatcher.Plugin",
            "bin",
            "Release",
            "FFXIVSpanishTranslationManager",
            "latest.zip");
        Assert.True(File.Exists(package), $"No se generó paquete plugin: {package}");
        return package;
    }
}
