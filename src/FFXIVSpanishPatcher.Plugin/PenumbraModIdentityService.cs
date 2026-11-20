using System.Text.Json;
using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Stable identity of the tree managed by this plugin.</summary>
public static class PenumbraModIdentity
{
    public const string CanonicalDirectory = "FFXIVSpanish";
    public const string LogicalName = "FFXIV en Español";
    public const string Website = "https://ffxivspanish.carrd.co/";
    public const string ManagedTag = "ffxiv-spanish-managed";
}

public enum ModIdentityResolutionKind
{
    NewInstallation,
    AdoptExisting,
    Ambiguous,
    Unsafe,
}

public sealed record ModIdentityCandidate(string DirectoryName, string FullPath, int FileVersion);

public sealed record ModIdentityResolution(
    ModIdentityResolutionKind Kind,
    IReadOnlyList<ModIdentityCandidate> Candidates,
    string Message)
{
    public ModIdentityCandidate? Selected => Kind == ModIdentityResolutionKind.AdoptExisting
        ? Candidates.Single()
        : null;
}

/// <summary>
/// Remembers the last resolution so the plugin does not read every mod's <c>meta.json</c> on the
/// game thread each time Penumbra's availability is reconciled. The remembered answer is reused
/// only while Penumbra reports the same mod root and the same set of mod directories and the
/// candidates are still on disk; a mod added, deleted, moved or rediscovered changes that set and
/// forces a fresh scan. Edits that keep the set intact (this plugin's own regeneration) must call
/// <see cref="Invalidate"/>. The transactional installer never uses this: it always scans.
/// </summary>
public sealed class CachedModIdentityResolver(IPenumbraModRegistry registry)
{
    private volatile Entry? _entry;

    public ModIdentityResolution Resolve(string modRootPath)
        => TryGetCached(modRootPath, out var resolution, out var directories)
            ? resolution
            : Scan(modRootPath, directories);

    /// <summary>
    /// The cheap half: one Penumbra call and a few directory checks, no metadata read. True with
    /// the answer when it is already known; false with the directory set that <see cref="Scan"/>
    /// must read. An unusable mod root is answered here, so it never needs a scan.
    /// </summary>
    public bool TryGetCached(string modRootPath, out ModIdentityResolution resolution, out string[] directories)
    {
        directories = registry.GetModList().Keys.Order(StringComparer.Ordinal).ToArray();
        if (PenumbraModIdentityService.CheckRoot(modRootPath, out _) is { } unsafeRoot)
        {
            resolution = unsafeRoot;
            return true;
        }

        if (_entry is { } entry
            && StringComparer.Ordinal.Equals(entry.ModRootPath, modRootPath)
            && entry.Directories.AsSpan().SequenceEqual(directories)
            && entry.Resolution.Candidates.All(candidate => Directory.Exists(candidate.FullPath)))
        {
            resolution = entry.Resolution;
            return true;
        }

        resolution = null!;
        return false;
    }

    /// <summary>
    /// The expensive half: reads the metadata of every listed mod and remembers the answer. It
    /// makes no Penumbra call, so the plugin runs it off the game thread.
    /// </summary>
    public ModIdentityResolution Scan(string modRootPath, string[] directories)
    {
        var resolution = PenumbraModIdentityService.Resolve(modRootPath, directories);
        // An unsafe root is a condition of the filesystem, not of the mod list: never remember it.
        _entry = resolution.Kind == ModIdentityResolutionKind.Unsafe
            ? null
            : new Entry(modRootPath, directories, resolution);
        return resolution;
    }

    public void Invalidate() => _entry = null;

    private sealed record Entry(string ModRootPath, string[] Directories, ModIdentityResolution Resolution);
}

/// <summary>
/// Finds only an unequivocal installation within Penumbra's own mod root. Paths supplied by IPC
/// are treated as untrusted even though Penumbra normally supplies them.
/// </summary>
public sealed class PenumbraModIdentityService(IPenumbraModRegistry registry)
{
    public ModIdentityResolution Resolve(string modRootPath)
        => Resolve(modRootPath, registry.GetModList().Keys);

    /// <summary>
    /// The filesystem half of the resolution: reads the metadata of the given mod directories.
    /// It makes no Penumbra call, so it may run off the game thread.
    /// </summary>
    public static ModIdentityResolution Resolve(string modRootPath, IEnumerable<string> directoryNames)
    {
        if (CheckRoot(modRootPath, out var root) is { } unsafeRoot)
        {
            return unsafeRoot;
        }

        var candidates = new List<ModIdentityCandidate>();
        foreach (var directoryName in directoryNames)
        {
            if (!IsSafeDirectoryName(directoryName))
            {
                continue;
            }

            var path = Path.Combine(root, directoryName);
            if (!Directory.Exists(path) || new DirectoryInfo(path).LinkTarget is not null)
            {
                continue;
            }

            if (TryReadManagedMetadata(path, out var candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates.Count switch
        {
            0 => new ModIdentityResolution(ModIdentityResolutionKind.NewInstallation, [],
                "No hay una instalación anterior que adoptar."),
            1 => new ModIdentityResolution(ModIdentityResolutionKind.AdoptExisting, candidates,
                $"Se adoptará el mod existente en {candidates[0].DirectoryName}."),
            _ => new ModIdentityResolution(ModIdentityResolutionKind.Ambiguous, candidates,
                "Se han detectado varias copias de la traducción; una regeneración las consolidará."),
        };
    }

    public static bool IsSafeDirectoryName(string? directoryName)
        => !String.IsNullOrWhiteSpace(directoryName)
            && string.Equals(directoryName, Path.GetFileName(directoryName), StringComparison.Ordinal)
            && directoryName.IndexOfAny(['/', '\\']) < 0
            && directoryName is not "." and not "..";

    /// <summary>Null when the mod root is usable; otherwise the <c>Unsafe</c> resolution that says why.</summary>
    public static ModIdentityResolution? CheckRoot(string modRootPath, out string root)
        => TryGetSafeRoot(modRootPath, out root, out var error)
            ? null
            : new ModIdentityResolution(ModIdentityResolutionKind.Unsafe, [], error);

    private static bool TryGetSafeRoot(string path, out string root, out string error)
    {
        root = string.Empty;
        error = string.Empty;
        if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            error = "El directorio de mods de Penumbra no existe.";
            return false;
        }

        var directory = new DirectoryInfo(path);
        if (directory.LinkTarget is not null)
        {
            error = "El directorio de mods de Penumbra no puede ser un enlace simbólico.";
            return false;
        }

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return true;
    }

    private static bool TryReadManagedMetadata(string fullPath, out ModIdentityCandidate candidate)
    {
        candidate = null!;
        var metaPath = Path.Combine(fullPath, "meta.json");
        if (!File.Exists(metaPath) || new FileInfo(metaPath).LinkTarget is not null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(metaPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var tagged = root.TryGetProperty("ModTags", out var tags)
                && tags.ValueKind == JsonValueKind.Array
                && tags.EnumerateArray().Any(tag => StringComparer.Ordinal.Equals(StringOrNull(tag), PenumbraModIdentity.ManagedTag));
            if (!tagged && !TranslationModIdentity.HasValidMarker(fullPath) && !HasV050CategoryFingerprint(root))
            {
                return false;
            }

            var fileVersion = IntOrNull(root, "FileVersion") ?? 0;
            candidate = new ModIdentityCandidate(Path.GetFileName(fullPath), fullPath, fileVersion);
            return true;
        }
        // Every mod in the library is read here, and almost all belong to someone else: content
        // this plugin cannot parse or open means "not ours", never a failure of the whole scan.
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // TryGetInt32() and GetString() throw on a value of another kind, and other authors'
    // metadata may hold any of them.
    private static int? IntOrNull(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;

    private static string? StringOrNull(JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    /// <summary>
    /// v0.5 packages cannot carry our private tag. Their human-readable name, author and website
    /// are intentionally mutable, so they are never used as identity. Instead recognize only the
    /// exact categorized tree shape emitted by the v0.5 patcher.
    /// </summary>
    private static bool HasV050CategoryFingerprint(JsonElement metadata)
    {
        if (!IsFileVersion(metadata, 4)
            || !metadata.TryGetProperty("Groups", out var groups)
            || groups.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var matchingGroups = groups.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("Name", out var name)
            && StringComparer.Ordinal.Equals(StringOrNull(name), TranslationCategoryCatalog.GroupName)).ToArray();
        if (matchingGroups.Length != 1)
        {
            return false;
        }

        var group = matchingGroups[0];
        if (group.ValueKind != JsonValueKind.Object
            || !group.TryGetProperty("Type", out var type)
            || !StringComparer.Ordinal.Equals(StringOrNull(type), "Multi")
            || !group.TryGetProperty("Options", out var options)
            || options.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var expected = TranslationCategoryCatalog.All
            .ToDictionary(category => category.DisplayName, StringComparer.Ordinal);
        var recognized = 0;
        foreach (var option in options.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object
                || !option.TryGetProperty("Name", out var name)
                || !expected.TryGetValue(StringOrNull(name) ?? string.Empty, out var category)
                || !option.TryGetProperty("Files", out var files)
                || files.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var file in files.EnumerateObject())
            {
                if (file.Value.ValueKind != JsonValueKind.String
                    || !IsExpectedRedirect(file.Name, file.Value.GetString(),
                        $"files/categories/{category.Order:D2}-{category.Domain}/"))
                {
                    return false;
                }

                recognized++;
            }

            expected.Remove(category.DisplayName);
        }

        // Every stable option must be present. A regular v0.5 build has hundreds of redirects;
        // the count guards against a look-alike metadata-only mod.
        return expected.Count == 0 && recognized >= 25;
    }

    private static bool IsFileVersion(JsonElement metadata, int expected)
        => IntOrNull(metadata, "FileVersion") == expected;

    private static bool IsExpectedRedirect(string gamePath, string? modPath, string prefix)
        => gamePath.StartsWith("exd/", StringComparison.Ordinal)
            && gamePath.EndsWith(".exd", StringComparison.OrdinalIgnoreCase)
            && modPath is not null
            && StringComparer.Ordinal.Equals(modPath, prefix + gamePath);
}
