using System.Buffers.Binary;

namespace XivSpanish.GameData;

/// <summary>
/// Structural location of one row block inside an EXD page, resolved from the index table.
/// <see cref="IsSupported"/> is true only for an intact <c>Default</c>-variant row
/// (<see cref="SubRowCount"/> == 1, non-negative string blob, whole block inside the buffer);
/// consumers must copy unsupported rows verbatim (patcher) or skip them (readers).
/// </summary>
/// <param name="RowId">Row id from the index entry.</param>
/// <param name="Offset">Absolute file offset of the 6-byte row header.</param>
/// <param name="DataSize">Declared row data size (bytes after the row header).</param>
/// <param name="SubRowCount">Declared row count: 1 for Default variant, N subrows otherwise.</param>
/// <param name="FixedStart">Absolute offset of the fixed column segment.</param>
/// <param name="StringStart">Absolute offset of the string heap (fixed segment end).</param>
/// <param name="StringLength">String heap length in bytes (negative on malformed rows).</param>
/// <param name="IsSupported">Whether the row is an intact Default-variant row.</param>
public readonly record struct ExdRowInfo(
    uint RowId,
    uint Offset,
    int DataSize,
    ushort SubRowCount,
    int FixedStart,
    int StringStart,
    int StringLength,
    bool IsSupported);

/// <summary>
/// Shared low-level EXD page structure: header magic, index table and row-block geometry.
/// All EXD integers are big-endian. This is the single definition of the row walk used by
/// <see cref="ExdPatcher"/> (read+write) and <see cref="ExdRowReader"/> (read-only), so the
/// two can never diverge on what constitutes a valid row.
/// </summary>
public static class ExdPage
{
    /// <summary>Fixed EXDF header size; the row-offset index table starts here.</summary>
    public const int HeaderSize = 0x20;

    /// <summary>Row header: u32 DataSize + u16 RowCount.</summary>
    public const int RowHeaderSize = 6;

    /// <summary>Row blocks are padded to this alignment.</summary>
    public const int RowAlignment = 4;

    /// <summary>File offset of the u32 body/data-section size field in the EXDF header.</summary>
    public const int DataSectionSizeOffset = 0x0C;

    /// <summary>True when <paramref name="bytes"/> starts with the <c>EXDF</c> magic and a full header.</summary>
    public static bool HasExdfMagic(byte[] bytes)
        => bytes.Length >= HeaderSize
        && bytes[0] == (byte)'E' && bytes[1] == (byte)'X'
        && bytes[2] == (byte)'D' && bytes[3] == (byte)'F';

    /// <summary>Number of index entries (rows) declared by the header's IndexSize.</summary>
    public static int ReadRowCount(byte[] bytes)
        => (int)(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0x08, 4)) / 8);

    /// <summary>Reads the i-th index entry: row id + absolute row-block offset.</summary>
    public static (uint RowId, uint Offset) ReadIndexEntry(byte[] bytes, int index)
    {
        var entryPos = HeaderSize + (index * 8);
        var rowId = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entryPos, 4));
        var offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entryPos + 4, 4));
        return (rowId, offset);
    }

    /// <summary>
    /// Resolves an index entry to its row-block geometry, or null when the declared offset
    /// does not even fit a row header (the caller cannot safely read anything at all).
    /// </summary>
    public static ExdRowInfo? TryReadRow(byte[] bytes, int fixedDataSize, uint rowId, uint offset)
    {
        if (offset > int.MaxValue || (long)offset + RowHeaderSize > bytes.Length)
        {
            return null;
        }

        var dataSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)offset, 4));
        var subRowCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan((int)offset + 4, 2));

        var fixedStart = (int)offset + RowHeaderSize;
        var stringStart = fixedStart + fixedDataSize;
        var stringLength = dataSize - fixedDataSize;

        // The whole declared row block (header + data) must fit in the buffer; otherwise the
        // fixed data or the string blob cannot be read safely.
        var rowBlockEnd = (long)offset + RowHeaderSize + Math.Max(dataSize, 0);
        var rowFits = stringStart <= bytes.Length
            && rowBlockEnd <= bytes.Length
            && fixedStart + fixedDataSize <= bytes.Length;

        var supported = subRowCount == 1 && stringLength >= 0 && rowFits;
        return new ExdRowInfo(rowId, offset, dataSize, subRowCount, fixedStart, stringStart, stringLength, supported);
    }

    /// <summary>
    /// Reads the raw NUL-terminated byte sequence at <paramref name="start"/> within a string
    /// heap, without decoding. Preserving raw bytes is required so binary SeString payloads
    /// (bytes that are not valid UTF-8) are never lossily round-tripped through a string.
    /// Returns an empty array for an out-of-range offset (some rows legitimately point at
    /// empty space; see AGENTS.md technical lessons).
    /// </summary>
    public static byte[] ReadNulTerminatedBytes(ReadOnlySpan<byte> area, int start)
    {
        if (start < 0 || start >= area.Length)
        {
            return [];
        }

        var end = start;
        while (end < area.Length && area[end] != 0)
        {
            end++;
        }

        return area[start..end].ToArray();
    }
}
