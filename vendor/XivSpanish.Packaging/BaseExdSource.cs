using XivSpanish.GameData;

namespace XivSpanish.Packager;

/// <summary>
/// Source of the BASE (pre-patch) EXD bytes and the per-sheet string layout the packager
/// patches against. Production reads them from the installed client
/// (<see cref="ClientExdSource"/>); tests supply synthetic pages built in code.
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
