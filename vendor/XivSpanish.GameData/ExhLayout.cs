using System.Buffers.Binary;

namespace XivSpanish.GameData;

/// <summary>
/// Layout an <see cref="ExdPatcher"/> needs to patch one sheet: the fixed row size, the
/// fixed-data offsets of every String column, and the EXH variant (1 = Default rows,
/// 2 = subrows — each subrow is prefixed by a u16 id, a layout the patcher does NOT support,
/// so variant-2 sheets must be skipped before patching; 0 = unknown for callers that cannot
/// supply it, treated as Default).
/// </summary>
public readonly record struct ExdLayout(int FixedDataSize, IReadOnlyList<int> StringColumnOffsets, int Variant = 1);

/// <summary>
/// Minimal parser for the fields an <see cref="ExdPatcher"/> needs out of an EXH header:
/// the fixed-data size and the offsets of String-typed columns. All EXH integers are
/// big-endian. Avoids a Lumina dependency so a vanilla snapshot is self-contained.
/// </summary>
public static class ExhLayout
{
    private const int ColumnDefsOffset = 0x20;
    private const int ColumnDefSize = 4;
    private const int VariantOffset = 0x11; // u8: 1 = Default rows, 2 = subrows
    private const ushort StringColumnType = 0; // ExcelColumnDataType.String

    public static ExdLayout? Parse(byte[] exh)
    {
        if (exh.Length < ColumnDefsOffset || exh[0] != (byte)'E' || exh[1] != (byte)'X'
            || exh[2] != (byte)'H' || exh[3] != (byte)'F')
        {
            return null;
        }

        var fixedDataSize = BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(0x06, 2));
        var columnCount = BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(0x08, 2));
        var variant = exh[VariantOffset];

        var stringOffsets = new List<int>();
        for (var i = 0; i < columnCount; i++)
        {
            var pos = ColumnDefsOffset + (i * ColumnDefSize);
            if (pos + ColumnDefSize > exh.Length)
            {
                break;
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(pos, 2));
            var offset = BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(pos + 2, 2));
            if (type == StringColumnType)
            {
                stringOffsets.Add(offset);
            }
        }

        return new ExdLayout(fixedDataSize, stringOffsets, variant);
    }
}
