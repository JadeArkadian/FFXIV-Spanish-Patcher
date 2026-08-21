using System.Text.Json;

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
    public ModIdentityResolution Resolve(string modRootPath, string? selectedExistingDirectory = null)
    {
        if (!TryGetSafeRoot(modRootPath, out var root, out var rootError))
        {
            return new ModIdentityResolution(ModIdentityResolutionKind.Unsafe, [], rootError);
        }

        var candidates = new List<ModIdentityCandidate>();
        foreach (var (directoryName, listedName) in registry.GetModList())
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

            if (TryReadManagedMetadata(path, listedName, out var candidate))
            {
                candidates.Add(candidate);
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedExistingDirectory))
        {
            var selected = candidates.SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.DirectoryName, selectedExistingDirectory));
            return selected is null
                ? new ModIdentityResolution(ModIdentityResolutionKind.Unsafe, candidates,
                    "La instalación seleccionada no es una candidata segura de FFXIV en Español.")
                : new ModIdentityResolution(ModIdentityResolutionKind.AdoptExisting, [selected],
                    $"Se adoptará el mod existente en {selected.DirectoryName}.");
        }

        return candidates.Count switch
        {
            0 => new ModIdentityResolution(ModIdentityResolutionKind.NewInstallation, [],
                "No hay una instalación anterior que adoptar."),
            1 => new ModIdentityResolution(ModIdentityResolutionKind.AdoptExisting, candidates,
                $"Se adoptará el mod existente en {candidates[0].DirectoryName}."),
            _ => new ModIdentityResolution(ModIdentityResolutionKind.Ambiguous, candidates,
                "Hay varias instalaciones candidatas; selecciona una explícitamente antes de actualizar."),
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

    private static bool TryReadManagedMetadata(string fullPath, string listedName, out ModIdentityCandidate candidate)
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
            var metaName = root.TryGetProperty("Name", out var name) ? name.GetString() : null;
            var website = root.TryGetProperty("Website", out var websiteValue) ? websiteValue.GetString() : null;
            var tagged = root.TryGetProperty("ModTags", out var tags)
                && tags.ValueKind == JsonValueKind.Array
                && tags.EnumerateArray().Any(tag => StringComparer.Ordinal.Equals(tag.GetString(), PenumbraModIdentity.ManagedTag));
            var nameMatches = StringComparer.Ordinal.Equals(listedName, PenumbraModIdentity.LogicalName)
                || StringComparer.Ordinal.Equals(metaName, PenumbraModIdentity.LogicalName);
            if (!tagged && !(nameMatches && StringComparer.Ordinal.Equals(website, PenumbraModIdentity.Website)))
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
}
