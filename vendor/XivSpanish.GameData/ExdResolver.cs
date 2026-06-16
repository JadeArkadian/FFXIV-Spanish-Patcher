using Lumina.Data;
using Lumina.Data.Files.Excel;
using LuminaGameData = Lumina.GameData;

namespace XivSpanish.GameData;

/// <summary>
/// Concrete EXD page that backs a sheet row, including the game-relative path a
/// Penumbra <c>default_mod.json</c> redirect must target.
/// </summary>
/// <param name="GamePath">Lowercased game path, e.g. <c>exd/enpcresident_0_en.exd</c>.</param>
/// <param name="PageStartId">First row id of the page.</param>
/// <param name="RowCount">Row count declared by the page.</param>
/// <param name="Language">Resolved Lumina language for the page suffix.</param>
public sealed record ExdLocation(string GamePath, uint PageStartId, uint RowCount, Language Language);

/// <summary>
/// Resolves which EXD page file contains a given sheet row, reading the EXH header
/// so paging is never assumed. Shared by the extractor and the packager.
/// </summary>
public sealed class ExdResolver
{
    private readonly LuminaGameData _gameData;

    public ExdResolver(LuminaGameData gameData) => _gameData = gameData;

    /// <summary>The Lumina game data this resolver reads from. Exposed so callers that need
    /// typed sheet metadata (e.g. physical column alignment) can reuse the open client.</summary>
    public LuminaGameData GameData => _gameData;

    /// <summary>Reads the EXH header for a sheet, or null if it does not exist.</summary>
    public ExcelHeaderFile? ReadHeader(string sheet)
        => _gameData.GetFile<ExcelHeaderFile>($"exd/{sheet}.exh");

    /// <summary>Reads the raw bytes of a game file (e.g. an EXD page), or null if missing.</summary>
    public byte[]? ReadRawFile(string gamePath)
        => _gameData.GetFile(gamePath)?.Data;

    /// <summary>
    /// Fixed row size and the fixed-data offsets of every String column for a sheet,
    /// the layout an <see cref="ExdPatcher"/> needs. Returns null if the sheet is missing.
    /// </summary>
    public ExdLayout? ReadStringLayout(string sheet)
    {
        var header = ReadHeader(sheet);
        if (header is null)
        {
            return null;
        }

        var stringColumns = header.ColumnDefinitions
            .Where(column => column.Type == Lumina.Data.Structs.Excel.ExcelColumnDataType.String)
            .Select(column => (int)column.Offset)
            .ToArray();

        return new ExdLayout(header.Header.DataOffset, stringColumns, (int)header.Header.Variant);
    }

    /// <summary>
    /// Resolves the EXD page path for <paramref name="rowId"/> in <paramref name="sheet"/>
    /// at the requested language. Returns null if the sheet or page cannot be resolved.
    /// </summary>
    public ExdLocation? Resolve(string sheet, uint rowId, string languageCode)
    {
        var header = ReadHeader(sheet);
        if (header is null)
        {
            return null;
        }

        // DataPages is an array of structs, so FirstOrDefault yields a zeroed page (RowCount 0)
        // when no page contains the row. Track the match explicitly so "no match" is
        // unambiguous and a real page with RowCount 0 cannot be mistaken for a hit. The
        // StartId + RowCount sum is widened to avoid u32 wraparound on the last page.
        var found = false;
        var page = header.DataPages.FirstOrDefault();
        foreach (var candidate in header.DataPages)
        {
            if (candidate.RowCount > 0 && rowId >= candidate.StartId
                && rowId < (long)candidate.StartId + candidate.RowCount)
            {
                page = candidate;
                found = true;
                break;
            }
        }

        if (!found)
        {
            return null;
        }

        var language = ResolveLanguage(header.Languages, languageCode);
        return new ExdLocation(BuildGamePath(sheet, page.StartId, language), page.StartId, page.RowCount, language);
    }

    /// <summary>Builds the lowercased game path of an EXD page.</summary>
    public static string BuildGamePath(string sheet, uint pageStartId, Language language)
    {
        var suffix = LanguageCodes.Suffix(language);
        var suffixPart = suffix.Length == 0 ? string.Empty : $"_{suffix}";
        return $"exd/{sheet}_{pageStartId}{suffixPart}.exd".ToLowerInvariant();
    }

    private static Language ResolveLanguage(Language[] languages, string languageCode)
    {
        var requested = LanguageCodes.ToLumina(languageCode);
        if (Array.IndexOf(languages, requested) >= 0)
        {
            return requested;
        }

        if (Array.IndexOf(languages, Language.None) >= 0 || languages.Length == 0)
        {
            return Language.None;
        }

        return languages[0];
    }
}
