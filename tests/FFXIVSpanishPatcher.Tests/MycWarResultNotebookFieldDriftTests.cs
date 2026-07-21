using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using XivSpanish.GameData;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

/// <summary>
/// Regression for the MYCWarResultNotebook lore-progreso miss: the corpus was extracted before the
/// offset-correct field resolver, so its manifest <c>Field</c> labels are PERMUTED relative to the
/// on-disk EXH String-column offsets. The physical columns are [ordinal 0 = NameJP (offset 8),
/// ordinal 1 = Name (offset 0), ordinal 2 = Description (offset 4)], but the corpus labels the short
/// character name as <c>Description</c> and the long biography as <c>NameJP</c>. The packager passes
/// the offset-correct sidecar names, so the labels point at the wrong ordinal and every replacement
/// hard-missed (8 applied, 92 missed).
///
/// The row is doubly adversarial: the Description is a multi-payload SeString (many
/// <c>&lt;NewLine#n&gt;</c>) AND the character's short name appears verbatim inside it. A naive
/// content-match fallback would let the short name hijack the biography column. The fix re-targets a
/// mislabeled field replacement to the unique column whose FULL content equals its source — an exact
/// whole-column match, never a substring — so both apply to the right column and nothing is hijacked.
/// </summary>
public sealed class MycWarResultNotebookFieldDriftTests
{
    // MYCWarResultNotebook layout from the vanilla EXH: 3 String columns at these fixed-data offsets,
    // in EXH (physical/ordinal) order. ordinal 0 -> offset 8 (NameJP), 1 -> offset 0 (Name),
    // 2 -> offset 4 (Description).
    private static readonly int[] StringColumnOffsets = [8, 0, 4];
    private const int FixedDataSize = 32;

    // Offset-correct field names the packager derives (ResolveByOffset / the .fields.json sidecar).
    private static readonly string[] OffsetCorrectFieldNames = ["NameJP", "Name", "Description"];

    private const string JpName = "ロフィー・ピル・ポティトゥス";
    private const string ShortName = "Llofii pyr Potitus";

    // A genuine multi-payload biography that CONTAINS the short name verbatim (the collision trap).
    private const string DescriptionSource =
        "Race: Miqo'te<NewLine>Age: 19<NewLine#2>A former imperial mage, Llofii pyr Potitus deserted "
        + "the IVth Legion.<NewLine#3><NewLine#4>Llofii pyr Potitus surrendered to the Resistance.";

    private const string DescriptionTarget =
        "Raza: Miqo'te<NewLine>Edad: 19<NewLine#2>Antigua maga imperial, Llofii pyr Potitus desertó "
        + "de la Legión IV.<NewLine#3><NewLine#4>Llofii pyr Potitus se rindió a la Resistencia.";

    [Fact]
    public void PermutedFieldLabels_SelfCorrect_BothColumnsApplyWithoutHijack()
    {
        var exd = BuildRow();

        // Corpus labels are permuted: the short name is labeled Description (physically ordinal 2),
        // the long biography is labeled NameJP (physically ordinal 0). Both point at the wrong column.
        var replacements = new Dictionary<uint, IReadOnlyList<StringReplacement>>
        {
            [37] =
            [
                new StringReplacement(ShortName, "Llofii pyr Potito", Field: "Description"),
                new StringReplacement(DescriptionSource, DescriptionTarget, Field: "NameJP"),
            ],
        };

        var result = ExdPatcher.Patch(exd, FixedDataSize, StringColumnOffsets, replacements, OffsetCorrectFieldNames);

        // Both replacements self-correct to their real column and apply; nothing missed.
        Assert.Empty(result.Missed);
        Assert.Equal(2, result.Applied);

        // The short name landed in the Name column (ordinal 1), translated.
        Assert.Equal("Llofii pyr Potito", SeStringTreeTokenizer.TokenizeRawText(ReadColumn(result.Bytes, ordinal: 1)));

        // The multi-payload biography landed in the Description column (ordinal 2), tokenized
        // identically — and was NOT hijacked by the short-name replacement despite containing it.
        Assert.Equal(DescriptionTarget, SeStringTreeTokenizer.TokenizeRawText(ReadColumn(result.Bytes, ordinal: 2)));

        // The Japanese name column (ordinal 0) is untouched.
        Assert.Equal(JpName, SeStringTreeTokenizer.TokenizeRawText(ReadColumn(result.Bytes, ordinal: 0)));
    }

    private static byte[] ReadColumn(byte[] exd, int ordinal)
        => ExdRowReader
            .ReadRawStrings(exd, FixedDataSize, StringColumnOffsets)
            .Where(x => x.RowId == 37 && x.ColumnOrdinal == ordinal)
            .Select(x => x.Raw)
            .Single();

    // Builds a single-row Default-variant page with the three MYC String columns at their EXH offsets.
    private static byte[] BuildRow()
    {
        // ordinal -> (fixed-data offset, raw bytes)
        var columns = new (int Offset, byte[] Value)[]
        {
            (8, Encoding.UTF8.GetBytes(JpName)),   // ordinal 0 (NameJP)
            (0, Encoding.UTF8.GetBytes(ShortName)), // ordinal 1 (Name)
            (4, SeStringBytes(DescriptionSource)),  // ordinal 2 (Description, multi-payload)
        };

        using var blob = new MemoryStream();
        blob.WriteByte(0); // offset 0 = empty string terminator
        var blobOffsets = new Dictionary<string, uint>();
        var fixedData = new byte[FixedDataSize];

        foreach (var (offset, value) in columns)
        {
            uint stringOffset;
            if (value.Length == 0)
            {
                stringOffset = 0;
            }
            else
            {
                var key = Convert.ToHexString(value);
                if (!blobOffsets.TryGetValue(key, out stringOffset))
                {
                    stringOffset = (uint)blob.Length;
                    blob.Write(value);
                    blob.WriteByte(0);
                    blobOffsets[key] = stringOffset;
                }
            }

            BinaryPrimitives.WriteUInt32BigEndian(fixedData.AsSpan(offset, 4), stringOffset);
        }

        var stringBlob = blob.ToArray();
        var dataSize = FixedDataSize + stringBlob.Length;

        const int headerSize = 0x20;
        var dataStart = headerSize + 8; // one row in the offset table

        using var body = new MemoryStream();
        var rowHeader = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(rowHeader, (uint)dataSize);
        BinaryPrimitives.WriteUInt16BigEndian(rowHeader.AsSpan(4), 1);
        body.Write(rowHeader);
        body.Write(fixedData);
        body.Write(stringBlob);
        var pad = (4 - ((6 + dataSize) % 4)) % 4;
        for (var p = 0; p < pad; p++)
        {
            body.WriteByte(0);
        }

        var bodyBytes = body.ToArray();
        var output = new byte[dataStart + bodyBytes.Length];
        "EXDF"u8.CopyTo(output);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0x08), 8);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(ExdPage.DataSectionSizeOffset), (uint)bodyBytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(headerSize), 37u);                 // rowId 37
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(headerSize + 4), (uint)dataStart);  // offset
        bodyBytes.CopyTo(output, dataStart);
        return output;
    }

    // Builds the raw SeString bytes for a tokenized source whose only payloads are <NewLine> (02 10
    // 01 03). Verifies the round-trip so the synthetic column matches exactly what the patcher reads.
    private static byte[] SeStringBytes(string tokenizedSource)
    {
        var newline = new byte[] { 0x02, 0x10, 0x01, 0x03 };
        using var ms = new MemoryStream();
        var remaining = tokenizedSource;
        while (remaining.Length > 0)
        {
            var open = remaining.IndexOf("<NewLine", StringComparison.Ordinal);
            if (open < 0)
            {
                ms.Write(Encoding.UTF8.GetBytes(remaining));
                break;
            }

            ms.Write(Encoding.UTF8.GetBytes(remaining[..open]));
            ms.Write(newline);
            var close = remaining.IndexOf('>', open);
            remaining = remaining[(close + 1)..];
        }

        var bytes = ms.ToArray();
        Assert.Equal(tokenizedSource, SeStringTreeTokenizer.TokenizeRawText(bytes));
        return bytes;
    }
}
