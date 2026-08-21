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
/// Finds only an unequivocal installation within Penumbra's own mod root. Paths supplied by IPC
/// are treated as untrusted even though Penumbra normally supplies them.
/// </summary>
public sealed class PenumbraModIdentityService(IPenumbraModRegistry registry)
{
    public ModIdentityResolution Resolve(string modRootPath)
    {
        if (!TryGetSafeRoot(modRootPath, out var root, out var rootError))
        {
            return new ModIdentityResolution(ModIdentityResolutionKind.Unsafe, [], rootError);
        }

        var candidates = new List<ModIdentityCandidate>();
        foreach (var (directoryName, _) in registry.GetModList())
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
            var tagged = root.TryGetProperty("ModTags", out var tags)
                && tags.ValueKind == JsonValueKind.Array
                && tags.EnumerateArray().Any(tag => StringComparer.Ordinal.Equals(tag.GetString(), PenumbraModIdentity.ManagedTag));
            if (!tagged && !TranslationModIdentity.HasValidMarker(fullPath) && !HasV050CategoryFingerprint(root))
            {
                return false;
            }

            var fileVersion = root.TryGetProperty("FileVersion", out var version) && version.TryGetInt32(out var value)
                ? value
                : 0;
            candidate = new ModIdentityCandidate(Path.GetFileName(fullPath), fullPath, fileVersion);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

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
            && StringComparer.Ordinal.Equals(name.GetString(), TranslationCategoryCatalog.GroupName)).ToArray();
        if (matchingGroups.Length != 1)
        {
            return false;
        }

        var group = matchingGroups[0];
        if (group.ValueKind != JsonValueKind.Object
            || !group.TryGetProperty("Type", out var type)
            || !StringComparer.Ordinal.Equals(type.GetString(), "Multi")
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
                || !expected.TryGetValue(name.GetString() ?? string.Empty, out var category)
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
        => metadata.TryGetProperty("FileVersion", out var version)
            && version.TryGetInt32(out var value)
            && value == expected;

    private static bool IsExpectedRedirect(string gamePath, string? modPath, string prefix)
        => gamePath.StartsWith("exd/", StringComparison.Ordinal)
            && gamePath.EndsWith(".exd", StringComparison.OrdinalIgnoreCase)
            && modPath is not null
            && StringComparer.Ordinal.Equals(modPath, prefix + gamePath);
}
