using System.Buffers.Binary;

namespace XivSpanish.GameData;

/// <summary>
/// Read-only enumerator over the String columns of a base (pre-patch) EXD page. Shares the row
/// walk with <see cref="ExdPatcher"/> via <see cref="ExdPage"/> but never writes: it exists so the
/// packager can discover every row whose source matches an approved translation and broadcast that
/// translation to duplicate rows the manifest does not list explicitly (the dedup/expand gap that
/// left e.g. Addon row 3256 untranslated while row 262 was patched), and so tests/validators can
/// inspect vanilla string bytes without re-implementing the format.
/// </summary>
public static class ExdRowReader
{
    /// <summary>
    /// Yields the RAW (undecoded) bytes of every non-empty String column of every Default-variant
    /// row. This is the byte-level view round-trip tests and corpus validators consume; subrow and
    /// malformed rows are skipped (the patcher does not support them either).
    /// </summary>
    public static IEnumerable<(uint RowId, int ColumnOrdinal, byte[] Raw)> ReadRawStrings(
        byte[] original,
        int fixedDataSize,
        IReadOnlyList<int> stringColumnOffsets)
    {
        if (!ExdPage.HasExdfMagic(original))
        {
            yield break;
        }

        var rowCount = ExdPage.ReadRowCount(original);
        for (var i = 0; i < rowCount; i++)
        {
            var (rowId, offset) = ExdPage.ReadIndexEntry(original, i);
            var row = ExdPage.TryReadRow(original, fixedDataSize, rowId, offset);
            if (row is not { IsSupported: true } info)
            {
                continue;
            }

            foreach (var column in ReadRowColumns(original, info, stringColumnOffsets))
            {
                yield return (rowId, column.Ordinal, column.Raw);
            }
        }
    }

    // Extracted so no Span lives across the iterator's yield (CS4007). Reads one row's String
    // columns into a list, then the caller yields them.
    private static List<(int Ordinal, byte[] Raw)> ReadRowColumns(
        byte[] original,
        ExdRowInfo row,
        IReadOnlyList<int> stringColumnOffsets)
    {
        var result = new List<(int, byte[])>();
        var fixedData = original.AsSpan(row.FixedStart, row.StringStart - row.FixedStart);
        var stringArea = original.AsSpan(row.StringStart, row.StringLength);

        for (var ordinal = 0; ordinal < stringColumnOffsets.Count; ordinal++)
        {
            var columnOffset = stringColumnOffsets[ordinal];
            if (columnOffset + 4 > fixedData.Length)
            {
                continue;
            }

            var strOffset = BinaryPrimitives.ReadUInt32BigEndian(fixedData.Slice(columnOffset, 4));
            var raw = ExdPage.ReadNulTerminatedBytes(stringArea, (int)strOffset);
            if (raw.Length > 0)
            {
                result.Add((ordinal, raw));
            }
        }

        return result;
    }
}
