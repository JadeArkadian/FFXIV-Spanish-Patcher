using System.Buffers.Binary;
using System.Text;

namespace FFXIVSpanishPatcher.Tests;

/// <summary>
/// Builds minimal but valid EXH headers and Default-variant EXD pages in memory, so the integration
/// test patches a real binary layout without ever versioning or reading a real game .exd (the
/// repo rule). Ported from the upstream patcher/reader test helpers.
/// </summary>
internal static class SyntheticExd
{
    private const int HeaderSize = 0x20;

    /// <summary>EXH header: magic, fixed row size, variant and a list of (type, offset) columns.
    /// String columns use type 0.</summary>
    public static byte[] BuildExh(ushort fixedDataSize, (ushort Type, ushort Offset)[] columns, byte variant = 1)
    {
        var exh = new byte[0x20 + (columns.Length * 4)];
        "EXHF"u8.CopyTo(exh);
        BinaryPrimitives.WriteUInt16BigEndian(exh.AsSpan(0x06), fixedDataSize);
        BinaryPrimitives.WriteUInt16BigEndian(exh.AsSpan(0x08), (ushort)columns.Length);
        exh[0x11] = variant;
        for (var i = 0; i < columns.Length; i++)
        {
            var pos = 0x20 + (i * 4);
            BinaryPrimitives.WriteUInt16BigEndian(exh.AsSpan(pos), columns[i].Type);
            BinaryPrimitives.WriteUInt16BigEndian(exh.AsSpan(pos + 2), columns[i].Offset);
        }

        return exh;
    }

    /// <summary>Default-variant EXD page with a single String column at fixed offset 0. An empty
    /// row text leaves that column pointing at an empty slot (the write-at-offset case).</summary>
    public static byte[] BuildExd((uint RowId, string Text)[] rows, int fixedSize = 4)
        => BuildExd(rows.Select(row => (row.RowId, Texts: new[] { row.Text })).ToArray(), fixedSize);

    /// <summary>Default-variant EXD page with one or more String columns at fixed offsets 0,4,...
    /// Each column points into that row's string area, matching the layout <see cref="ExdPatcher"/>
    /// consumes.</summary>
    public static byte[] BuildExd((uint RowId, string[] Texts)[] rows, int fixedSize)
        => BuildExdRaw(
            rows.Select(row => (row.RowId, RawTexts: row.Texts.Select(Encoding.UTF8.GetBytes).ToArray())).ToArray(),
            fixedSize);

    /// <summary>Default-variant EXD page whose String columns are already raw SeString bytes
    /// without NUL terminators. Used when a test needs real payload/run bytes.</summary>
    public static byte[] BuildExdRaw((uint RowId, byte[][] RawTexts)[] rows, int fixedSize)
    {
        var columnCount = rows.Length == 0 ? 0 : rows.Max(row => row.RawTexts.Length);
        if (fixedSize < columnCount * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedSize), "Fixed size must hold every string offset.");
        }

        var dataStart = HeaderSize + (rows.Length * 8);

        using var body = new MemoryStream();
        var offsets = new uint[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            offsets[i] = (uint)(dataStart + body.Length);
            var fixedData = new byte[fixedSize];
            using var stringBlob = new MemoryStream();
            for (var column = 0; column < rows[i].RawTexts.Length; column++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(fixedData.AsSpan(column * 4, 4), (uint)stringBlob.Length);
                stringBlob.Write(rows[i].RawTexts[column]);
                stringBlob.WriteByte(0);
            }

            var header = new byte[6];
            var stringBytesForRow = stringBlob.ToArray();
            var dataSize = fixedSize + stringBytesForRow.Length;
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)dataSize);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 1);
            body.Write(header);
            body.Write(fixedData);
            body.Write(stringBytesForRow);

            var pad = (4 - ((6 + dataSize) % 4)) % 4;
            for (var p = 0; p < pad; p++)
            {
                body.WriteByte(0);
            }
        }

        var bodyBytes = body.ToArray();
        var output = new byte[dataStart + bodyBytes.Length];
        "EXDF"u8.CopyTo(output);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0x08), (uint)(rows.Length * 8));
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0x0C), (uint)bodyBytes.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(HeaderSize + (i * 8)), rows[i].RowId);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(HeaderSize + (i * 8) + 4), offsets[i]);
        }

        bodyBytes.CopyTo(output, dataStart);
        return output;
    }
}
