using System.Text.Json;
using XivSpanish.GameData;

namespace XivSpanish.Packager;

/// <summary>
/// Source of the BASE (pre-patch) EXD bytes and the per-sheet string layout the packager
/// patches against. Decouples packaging from the live FFXIV client: the bytes can come from
/// the installed client (<see cref="ClientExdSource"/>) or from an immutable vanilla snapshot
/// on disk (<see cref="DirectoryExdSource"/>), making packaging reproducible.
/// </summary>
public interface IBaseExdSource
{
    /// <summary>Raw bytes of a base EXD page (e.g. <c>exd/addon_0_en.exd</c>), or null if missing.</summary>
    byte[]? ReadBaseExd(string exdPath);

    /// <summary>Fixed row size and String-column offsets for a sheet, or null if missing.</summary>
    ExdLayout? ReadStringLayout(string sheet);

    /// <summary>
    /// Ordered string-column field names for a sheet, where the i-th name belongs to the i-th EXH
    /// String column. The patcher uses these to target a replacement at its exact column by its
    /// source-key <c>Field</c>. Implementations align each generated member to its physical column
    /// when the live client is available (correct even when declaration order is permuted relative
    /// to on-disk offsets, e.g. <c>TextCommand</c>), otherwise fall back to the reflection-only
    /// declaration-order resolution.
    /// </summary>
    IReadOnlyList<string> ResolveFieldNames(string sheet, int stringColumnCount);
}

/// <summary>Reads base EXD bytes and layout from the installed FFXIV client via Lumina.</summary>
public sealed class ClientExdSource(ExdResolver resolver) : IBaseExdSource
{
    private readonly ExdResolver _resolver = resolver;

    public byte[]? ReadBaseExd(string exdPath) => _resolver.ReadRawFile(exdPath);

    public ExdLayout? ReadStringLayout(string sheet) => _resolver.ReadStringLayout(sheet);

    // Prefer the offset-correct alignment (uses the open client's typed sheet metadata); fall back
    // to reflection-only declaration order when the alignment cannot be determined unambiguously.
    public IReadOnlyList<string> ResolveFieldNames(string sheet, int stringColumnCount)
        => SheetStringFieldResolver.ResolveByOffset(_resolver.GameData, sheet, stringColumnCount)
            ?? SheetStringFieldResolver.Resolve(sheet, stringColumnCount);
}

/// <summary>
/// Reads base EXD bytes and EXH layout from a local vanilla snapshot directory, never touching
/// the live client. The snapshot is produced once by the packager's <c>--dump-base-exd</c> mode
/// from a clean client. EXH layout is parsed directly from the snapshot's <c>.exh</c> files (via
/// the shared <see cref="ExhLayout"/> parser) so a contaminated or absent client cannot influence
/// packaging.
/// </summary>
public sealed class DirectoryExdSource(string baseDirectory) : IBaseExdSource
{
    private readonly string _baseDirectory = baseDirectory;

    public byte[]? ReadBaseExd(string exdPath)
    {
        var path = ResolveLocalPath(exdPath);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public ExdLayout? ReadStringLayout(string sheet)
    {
        var path = ResolveLocalPath($"exd/{sheet}.exh");
        if (!File.Exists(path))
        {
            return null;
        }

        return ExhLayout.Parse(File.ReadAllBytes(path));
    }

    // Snapshot builds do not open the live client. When the snapshot was dumped by a recent
    // packager, use the offset-correct field map captured alongside the EXH; otherwise keep the
    // legacy reflection-only fallback for older snapshots.
    public IReadOnlyList<string> ResolveFieldNames(string sheet, int stringColumnCount)
    {
        var sidecar = ResolveLocalPath($"exd/{sheet}.fields.json");
        if (File.Exists(sidecar))
        {
            try
            {
                var names = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(sidecar));
                if (names is not null && names.Length == stringColumnCount && names.All(name => !string.IsNullOrWhiteSpace(name)))
                {
                    return names;
                }
            }
            catch (JsonException)
            {
                // Corrupt sidecar: fall back to the deterministic legacy resolver.
            }
        }

        return SheetStringFieldResolver.Resolve(sheet, stringColumnCount);
    }

    private string ResolveLocalPath(string gamePath)
        => Path.Combine(_baseDirectory, gamePath.Replace('/', Path.DirectorySeparatorChar));
}
