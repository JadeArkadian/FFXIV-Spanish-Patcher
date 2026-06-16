using System.IO.Compression;
using XivSpanish.GameData;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Post-build structural check of a generated <c>.pmp</c> ("Verificar integridad al finalizar").
/// Returns the list of problems found; an empty list means the package is structurally sound.
/// </summary>
public interface IIntegrityVerifier
{
    IReadOnlyList<string> Verify(string pmpPath, IReadOnlyDictionary<string, string> declaredFiles);
}

/// <summary>
/// Default verifier: re-opens the zip and asserts the Penumbra manifests are present, every declared
/// redirect file is in the archive, no entry uses an absolute/traversal path, and every declared
/// <c>.exd</c> still begins with the <c>EXDF</c> magic (i.e. the patcher produced a structurally
/// valid page, not a corrupt one).
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

        foreach (var manifest in new[] { "meta.json", "default_mod.json" })
        {
            if (!entries.ContainsKey(manifest))
            {
                problems.Add($"{manifest} missing at package root");
            }
        }

        foreach (var name in entries.Keys)
        {
            if (name.StartsWith('/') || name.Contains(':') || name.Contains(".."))
            {
                problems.Add($"unsafe package path: {name}");
            }
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
