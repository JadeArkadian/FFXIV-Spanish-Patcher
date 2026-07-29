using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FFXIVSpanishPatcher.App.Services;

public enum DalamudPenumbraState
{
    NotDetected,
    Ready,
    RequiresResumeAfterPluginLoad,
}

public sealed record DalamudPenumbraCheck(DalamudPenumbraState State, string? ConfigPath = null);

/// <summary>
/// Bounded, silent inspection of known XIVLauncher/XIV on Mac roots. Detection and writes are
/// deliberately best effort: callers never log or warn when third-party configuration is absent,
/// unreadable or concurrently changed.
/// </summary>
public sealed class DalamudPenumbraService
{
    private const string PropertyName = "IsResumeGameAfterPluginLoad";

    public DalamudPenumbraCheck Inspect()
    {
        foreach (var root in CandidateRoots())
        {
            var check = InspectRoot(root);
            if (check.State != DalamudPenumbraState.NotDetected)
            {
                return check;
            }
        }

        return new DalamudPenumbraCheck(DalamudPenumbraState.NotDetected);
    }

    public DalamudPenumbraCheck InspectRoot(string root)
    {
        try
        {
            var configPath = ConfigCandidates(root).FirstOrDefault(File.Exists);
            if (configPath is null || !HasPenumbraManifest(root))
            {
                return new DalamudPenumbraCheck(DalamudPenumbraState.NotDetected);
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(configPath));
            var ready = document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty(PropertyName, out var property)
                        && property.ValueKind == JsonValueKind.True;
            return new DalamudPenumbraCheck(
                ready ? DalamudPenumbraState.Ready : DalamudPenumbraState.RequiresResumeAfterPluginLoad,
                configPath);
        }
        catch
        {
            return new DalamudPenumbraCheck(DalamudPenumbraState.NotDetected);
        }
    }

    public bool TryEnableResumeAfterPluginLoad(DalamudPenumbraCheck check)
    {
        if (check.ConfigPath is not { Length: > 0 } configPath)
        {
            return false;
        }

        var temporaryPath = string.Empty;
        try
        {
            var original = File.ReadAllBytes(configPath);
            var root = JsonNode.Parse(original) as JsonObject;
            if (root is null)
            {
                return false;
            }

            root[PropertyName] = true;
            temporaryPath = Path.Combine(
                Path.GetDirectoryName(configPath)!,
                $".{Path.GetFileName(configPath)}.{Guid.NewGuid():N}.tmp");
            var updated = JsonSerializer.SerializeToUtf8Bytes(root, DalamudJsonContext.Default.JsonObject);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(updated);
                stream.Flush(flushToDisk: true);
            }

            // Abort silently if Dalamud rewrote its config while the patcher prepared the update.
            var current = File.ReadAllBytes(configPath);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(original), SHA256.HashData(current)))
            {
                return false;
            }

            File.Replace(temporaryPath, configPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            temporaryPath = string.Empty;
            return InspectRoot(Path.GetDirectoryName(configPath)!).State == DalamudPenumbraState.Ready
                   || ReadEnabled(configPath);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporaryPath.Length > 0)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Silent best effort by contract.
                }
            }
        }
    }

    private static bool ReadEnabled(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.TryGetProperty(PropertyName, out var property)
               && property.ValueKind == JsonValueKind.True;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher");
            yield break;
        }

        if (OperatingSystem.IsLinux())
        {
            yield return Path.Combine(userProfile, ".xlcore");
            yield return Path.Combine(userProfile, ".var", "app", "dev.goats.xivlauncher", ".xlcore");
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(userProfile, "Library", "Application Support", "XIV on Mac");
        }
    }

    private static IEnumerable<string> ConfigCandidates(string root)
    {
        yield return Path.Combine(root, "dalamudConfig.json");
        yield return Path.Combine(root, "dalamudconfig.json");
    }

    private static bool HasPenumbraManifest(string root)
    {
        var penumbraRoot = Path.Combine(root, "installedPlugins", "Penumbra");
        if (!Directory.Exists(penumbraRoot))
        {
            return false;
        }

        var direct = Path.Combine(penumbraRoot, "Penumbra.json");
        if (IsPenumbraManifest(direct))
        {
            return true;
        }

        foreach (var versionDirectory in Directory.EnumerateDirectories(penumbraRoot))
        {
            if (IsPenumbraManifest(Path.Combine(versionDirectory, "Penumbra.json")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPenumbraManifest(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        return IsPenumbraName(root, "InternalName") || IsPenumbraName(root, "Name");
    }

    private static bool IsPenumbraName(JsonElement root, string propertyName)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(propertyName, out var property)
           && string.Equals(property.GetString(), "Penumbra", StringComparison.OrdinalIgnoreCase);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(JsonObject))]
internal partial class DalamudJsonContext : JsonSerializerContext;
