using System.IO.Compression;
using System.Text;
using FFXIVSpanishPatcher.App.Services;

namespace FFXIVSpanishPatcher.App.Tests;

/// <summary>A stand-in for the plugin ZIP of a release: a fake DLL and its Dalamud manifest.</summary>
internal static class TestPluginPackage
{
    public const string Name = DalamudPenumbraService.PluginInternalName;

    public static byte[] Create(
        string internalName = Name,
        string version = "0.6.0.0",
        bool withDll = true,
        string? extraEntry = null)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (withDll)
            {
                Write(archive, Name + ".dll", "not a real assembly");
            }

            Write(
                archive,
                Name + ".json",
                $$"""{"Name":"FFXIV Spanish Translation Manager","InternalName":"{{internalName}}","AssemblyVersion":"{{version}}","DalamudApiLevel":15}""");
            if (extraEntry is not null)
            {
                Write(archive, extraEntry, "escaped");
            }
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}

internal sealed class FakePluginPackageSource(byte[]? package) : IPluginPackageSource
{
    public int Downloads { get; private set; }

    public Task<byte[]?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        Downloads++;
        return Task.FromResult(package);
    }
}
