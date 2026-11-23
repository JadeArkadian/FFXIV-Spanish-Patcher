using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.App.Services;

public enum DalamudPenumbraState
{
    NotDetected,
    Ready,
    RequiresResumeAfterPluginLoad,
}

public sealed record DalamudPenumbraCheck(DalamudPenumbraState State, string? ConfigPath = null);

public enum PluginRepositoryState
{
    DalamudNotDetected,
    Missing,
    Present,
}

/// <summary>
/// Bounded, silent inspection of known XIVLauncher/XIV on Mac roots. Detection and writes are
/// deliberately best effort: callers never log or warn when third-party configuration is absent,
/// unreadable or concurrently changed.
/// </summary>
public sealed class DalamudPenumbraService
{
    public const string PluginInternalName = "FFXIVSpanishTranslationManager";

    private const long MaxPluginBytes = 512L * 1024 * 1024;
    private const string PropertyName = DalamudStartupSettings.WaitForPluginsPropertyName;
    private const string RepositoryListPropertyName = "ThirdRepoList";

    // Dalamud serializes its configuration with Newtonsoft type names, which must come first.
    private const string RepositoryListTypeName =
        "System.Collections.Generic.List`1[[Dalamud.Configuration.ThirdPartyRepoSettings, Dalamud]], System.Private.CoreLib";
    private const string RepositoryTypeName = "Dalamud.Configuration.ThirdPartyRepoSettings, Dalamud";

    private readonly Func<bool> _isGameRunning;
    private readonly string? _root;

    /// <param name="root">Inspects only this Dalamud root instead of the known ones (tests).</param>
    public DalamudPenumbraService(Func<bool>? isGameRunning = null, string? root = null)
    {
        _isGameRunning = isGameRunning ?? DetectRunningGame;
        _root = root;
    }

    /// <summary>
    /// True while the game process exists. A running Dalamud owns its configuration in memory and
    /// rewrites the whole file on its next save, so an edit made meanwhile would be lost without a
    /// trace. Detection failures count as "not running": the edit stays best effort.
    /// </summary>
    public bool IsGameRunning()
    {
        try
        {
            return _isGameRunning();
        }
        catch
        {
            return false;
        }
    }

    // Under Wine/Proton the process keeps its Windows image name, extension included.
    private static bool DetectRunningGame()
    {
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName.StartsWith("ffxiv_dx11", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process ended between enumeration and inspection.
                }
            }
        }

        return false;
    }

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
        if (check.ConfigPath is not { Length: > 0 } configPath || IsGameRunning())
        {
            return false;
        }

        try
        {
            return UpdateConfig(configPath, root =>
                   {
                       root[PropertyName] = true;
                       return true;
                   })
                   && (InspectRoot(Path.GetDirectoryName(configPath)!).State == DalamudPenumbraState.Ready
                       || ReadEnabled(configPath));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether Dalamud already loads plugins from the custom repository at <paramref name="url"/>.</summary>
    public PluginRepositoryState InspectPluginRepository(string url)
    {
        try
        {
            if (FindConfigPath() is not { } configPath
                || JsonNode.Parse(File.ReadAllBytes(configPath)) is not JsonObject root)
            {
                return PluginRepositoryState.DalamudNotDetected;
            }

            var enabled = Repositories(root)?.OfType<JsonObject>().Any(repository =>
                IsRepository(repository, url)
                && repository["IsEnabled"] is JsonValue value
                && value.TryGetValue<bool>(out var isEnabled)
                && isEnabled);
            return enabled == true ? PluginRepositoryState.Present : PluginRepositoryState.Missing;
        }
        catch
        {
            return PluginRepositoryState.DalamudNotDetected;
        }
    }

    /// <summary>
    /// Adds (or re-enables) the custom plugin repository in Dalamud's configuration.
    /// </summary>
    public bool TryAddPluginRepository(string url)
    {
        if (IsGameRunning())
        {
            return false;
        }

        try
        {
            return FindConfigPath() is { } configPath
                   && UpdateConfig(configPath, root => AddRepository(root, url))
                   && InspectPluginRepository(url) == PluginRepositoryState.Present;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the patcher can take the user to the plugin right now: Dalamud is there, its
    /// configuration can be rewritten (game closed, file not locked or read-only), and neither the
    /// repository nor the plugin is in place yet.
    /// </summary>
    public bool CanInstallPlugin(string repositoryUrl)
        => !IsGameRunning()
           && InspectPluginRepository(repositoryUrl) == PluginRepositoryState.Missing
           && !IsPluginInstalled()
           && FindConfigPath() is { } configPath
           && IsWritable(configPath);

    private static bool IsWritable(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether Dalamud already has any version of the plugin on disk.</summary>
    public bool IsPluginInstalled()
    {
        try
        {
            return PluginDirectory() is { } directory
                   && Directory.Exists(directory)
                   && Directory.EnumerateDirectories(directory)
                       .Any(version => File.Exists(Path.Combine(version, PluginInternalName + ".dll")));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Leaves the packaged plugin where Dalamud's own installer would, so the next game start loads
    /// it. Dalamud has no install queue: it loads whatever <c>installedPlugins</c> holds and enables
    /// a plugin no profile knows. Never replaces an installed plugin; updating it is Dalamud's job.
    /// </summary>
    /// <returns>True when the plugin is installed, whether by this call or already.</returns>
    public bool InstallPlugin(byte[] package, string repositoryUrl)
    {
        if (IsGameRunning() || PluginDirectory() is not { } pluginDirectory)
        {
            return false;
        }

        if (IsPluginInstalled())
        {
            return true;
        }

        // Staged beside installedPlugins: same volume for an atomic move, outside what Dalamud scans.
        var staging = Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(pluginDirectory)!)!,
            $".{PluginInternalName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
            {
                if (archive.Entries.Sum(entry => entry.Length) > MaxPluginBytes)
                {
                    return false;
                }

                // Throws on an entry that would land outside the staging directory.
                archive.ExtractToDirectory(staging);
            }

            var manifestPath = Path.Combine(staging, PluginInternalName + ".json");
            if (!File.Exists(Path.Combine(staging, PluginInternalName + ".dll"))
                || !File.Exists(manifestPath)
                || JsonNode.Parse(File.ReadAllBytes(manifestPath)) is not JsonObject manifest
                || !string.Equals(ReadString(manifest, "InternalName"), PluginInternalName, StringComparison.Ordinal)
                || !Version.TryParse(ReadString(manifest, "AssemblyVersion"), out var version))
            {
                return false;
            }

            // What Dalamud's installer records: where updates come from and the installation id.
            manifest["InstalledFromUrl"] = repositoryUrl;
            manifest["WorkingPluginId"] = Guid.NewGuid().ToString();
            File.WriteAllBytes(
                manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(manifest, DalamudJsonContext.Default.JsonObject));

            Directory.CreateDirectory(pluginDirectory);
            Directory.Move(staging, Path.Combine(pluginDirectory, version.ToString()));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch
            {
                // Silent best effort by contract.
            }
        }
    }

    private string? PluginDirectory()
        => FindConfigPath() is { } configPath
            ? Path.Combine(Path.GetDirectoryName(configPath)!, "installedPlugins", PluginInternalName)
            : null;

    private static string? ReadString(JsonObject node, string name)
        => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool AddRepository(JsonObject root, string url)
    {
        var repositories = Repositories(root);
        if (repositories is null)
        {
            if (root[RepositoryListPropertyName] is not null)
            {
                // A shape this version does not know: leave it to the user.
                return false;
            }

            repositories = new JsonArray();
            root[RepositoryListPropertyName] = new JsonObject
            {
                ["$type"] = RepositoryListTypeName,
                ["$values"] = repositories,
            };
        }

        if (repositories.OfType<JsonObject>().FirstOrDefault(repository => IsRepository(repository, url))
            is { } existing)
        {
            existing["IsEnabled"] = true;
        }
        else
        {
            // Typed as JsonNode: the generic Add<T> overload is not trim-safe.
            JsonNode repository = new JsonObject
            {
                ["$type"] = RepositoryTypeName,
                ["Url"] = url,
                ["IsEnabled"] = true,
            };
            repositories.Add(repository);
        }

        return true;
    }

    private static JsonArray? Repositories(JsonObject root)
        => root[RepositoryListPropertyName] switch
        {
            JsonObject list => list["$values"] as JsonArray,
            JsonArray array => array,
            _ => null,
        };

    // Exact match, as Dalamud does when it pairs an installed plugin with its repository: an entry
    // spelled differently would leave the plugin orphaned and unloaded.
    private static bool IsRepository(JsonObject repository, string url)
        => repository["Url"] is JsonValue value
           && value.TryGetValue<string>(out var candidate)
           && string.Equals(candidate, url, StringComparison.Ordinal);

    /// <summary>
    /// Rewrites the configuration through a sibling temporary file and an atomic replace, and gives
    /// up if Dalamud changed the file meanwhile.
    /// </summary>
    private static bool UpdateConfig(string configPath, Func<JsonObject, bool> change)
    {
        var temporaryPath = string.Empty;
        try
        {
            var original = File.ReadAllBytes(configPath);
            if (JsonNode.Parse(original) is not JsonObject root || !change(root))
            {
                return false;
            }

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
            return true;
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

    private string? FindConfigPath()
        => CandidateRoots().Select(root => ConfigCandidates(root).FirstOrDefault(File.Exists))
            .FirstOrDefault(path => path is not null);

    private IEnumerable<string> CandidateRoots()
    {
        if (_root is not null)
        {
            yield return _root;
            yield break;
        }

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
