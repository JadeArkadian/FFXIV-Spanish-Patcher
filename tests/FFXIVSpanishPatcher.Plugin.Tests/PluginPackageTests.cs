using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using FFXIVSpanishPatcher.Pipeline;
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
        Assert.DoesNotContain("FFXIVSpanishTranslationManager.legal-and-credits.md", resourceNames);

        var packagePath = ReleasePackagePath();
        using var archive = ZipFile.OpenRead(packagePath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("FFXIVSpanishTranslationManager.dll", names);
        Assert.Contains("FFXIVSpanishTranslationManager.json", names);
        Assert.DoesNotContain(names, name => name is "Dalamud.dll" or "Lumina.dll" or "Lumina.Excel.dll"
            or "Microsoft.Extensions.ObjectPool.dll");
        // A folder inside the ZIP is a leftover such as bin/Release/publish, which would bring
        // back every excluded assembly under another path.
        Assert.DoesNotContain(names, name => name.Contains('/') || name.Contains('\\'));

        // The corpus inside the plugin DLL is already compressed, so the ZIP weighs about as much
        // as that DLL. Much more than that means something was packed twice.
        var pluginDll = archive.GetEntry("FFXIVSpanishTranslationManager.dll")!;
        var packageSize = new FileInfo(packagePath).Length;
        Assert.True(
            packageSize < pluginDll.Length + (8 * 1024 * 1024),
            $"El ZIP del plugin pesa {packageSize} bytes, demasiado para una DLL de {pluginDll.Length}.");

        using var manifest = JsonDocument.Parse(archive.GetEntry("FFXIVSpanishTranslationManager.json")!.Open());
        Assert.Equal("FFXIVSpanishTranslationManager", manifest.RootElement.GetProperty("InternalName").GetString());
        Assert.Equal(15, manifest.RootElement.GetProperty("DalamudApiLevel").GetInt32());
        Assert.True(manifest.RootElement.GetProperty("LoadSync").GetBoolean());
        Assert.True(manifest.RootElement.GetProperty("CanUnloadAsync").GetBoolean());
    }

    [Fact]
    public void ReleasePackage_ManifestAssemblyAndModMetadataShareOneVersion()
    {
        var assembly = typeof(PluginLoadProbe).Assembly;
        using var archive = ZipFile.OpenRead(ReleasePackagePath());
        using var manifest = JsonDocument.Parse(archive.GetEntry("FFXIVSpanishTranslationManager.json")!.Open());
        var manifestVersion = manifest.RootElement.GetProperty("AssemblyVersion").GetString();

        // Dalamud compares the manifest with the loaded assembly to decide whether an update is
        // pending, and the generated mod carries the same number in its own metadata.
        Assert.Equal(assembly.GetName().Version!.ToString(), manifestVersion);
        Assert.Equal(PackageMetadataFactory.PackageVersionFromAssembly(assembly) + ".0", manifestVersion);
    }

    [Fact]
    public void ReleasePackage_ShipsASquareIconDalamudAccepts()
    {
        var packageDirectory = Path.GetDirectoryName(ReleasePackagePath())!;
        using var manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(packageDirectory, "FFXIVSpanishTranslationManager.json")));
        Assert.EndsWith(
            "/src/FFXIVSpanishPatcher.Plugin/images/icon.png",
            manifest.RootElement.GetProperty("IconUrl").GetString(),
            StringComparison.Ordinal);

        // Two copies: beside the DLL for a dev plugin, beside the ZIP for the repository layout.
        var besideDll = Path.Combine(Path.GetDirectoryName(packageDirectory)!, "images", "icon.png");
        var besideZip = Path.Combine(packageDirectory, "images", "icon.png");
        Assert.True(File.Exists(besideDll), $"Falta el icono junto a la DLL: {besideDll}");
        Assert.True(File.Exists(besideZip), $"Falta el icono junto al ZIP: {besideZip}");

        // PNG stores width and height big-endian right after the IHDR tag. Dalamud rejects an
        // icon that is not square or is larger than 512 px.
        var header = File.ReadAllBytes(besideDll).AsSpan(0, 24);
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..]);
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..]);
        Assert.Equal(width, height);
        Assert.InRange(width, 64, 512);
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
