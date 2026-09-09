using System.IO.Compression;
using XivSpanish.GameData;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Mandatory post-build structural check of a generated Penumbra v4 <c>.pmp</c>.
/// Returns the list of problems found; an empty list means the package is structurally sound.
/// </summary>
public interface IIntegrityVerifier
{
    IReadOnlyList<string> Verify(string pmpPath, IReadOnlyDictionary<string, string> declaredFiles);
}

/// <summary>
/// Default verifier: re-opens the ZIP, validates the v4 category manifest against the generated
/// redirects, rejects legacy manifests and unsafe paths, and checks every declared EXD magic.
/// </summary>
public sealed class IntegrityVerifier : IIntegrityVerifier
{
    public IReadOnlyList<string> Verify(string pmpPath, IReadOnlyDictionary<string, string> declaredFiles)
    {
        var problems = new List<string>();

        if (!File.Exists(pmpPath))
        {
            return [$"package not found: {pmpPath}"];
        }

        using var archive = ZipFile.OpenRead(pmpPath);
        var entries = archive.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);

        if (!entries.TryGetValue("meta.json", out var meta))
        {
            problems.Add("meta.json missing at package root");
            return problems;
        }

        if (entries.ContainsKey("default_mod.json"))
        {
            problems.Add("legacy default_mod.json must not be in a v4 package");
        }

        foreach (var name in entries.Keys)
        {
            if (!PenumbraV4ManifestVerifier.IsSafeRelativePath(name))
            {
                problems.Add($"unsafe package path: {name}");
            }
        }

        try
        {
            using var stream = meta.Open();
            using var document = System.Text.Json.JsonDocument.Parse(stream);
            problems.AddRange(PenumbraV4ManifestVerifier.Verify(document.RootElement, declaredFiles, entries.ContainsKey));
        }
        catch (System.Text.Json.JsonException exception)
        {
            problems.Add($"invalid meta.json: {exception.Message}");
        }

        foreach (var modRelative in declaredFiles.Values)
        {
            if (!entries.TryGetValue(modRelative, out var entry))
            {
                problems.Add($"declared file not in package: {modRelative}");
                continue;
            }

            if (modRelative.EndsWith(".exd", StringComparison.OrdinalIgnoreCase) && !BeginsWithExdfMagic(entry))
            {
                problems.Add($"patched EXD is not a valid EXDF page: {modRelative}");
            }
        }

        return problems;
    }

    private static bool BeginsWithExdfMagic(ZipArchiveEntry entry)
    {
        var header = new byte[ExdPage.HeaderSize];
        using var stream = entry.Open();
        var read = 0;
        while (read < header.Length)
        {
            var n = stream.Read(header, read, header.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return read >= 4 && ExdPage.HasExdfMagic(header);
    }
}
