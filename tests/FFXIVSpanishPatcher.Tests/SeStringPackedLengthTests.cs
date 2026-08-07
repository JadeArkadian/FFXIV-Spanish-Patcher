using XivSpanish.GameData;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class SeStringPackedLengthTests
{
    [Fact]
    public void Parser_InlineLengthBoundaryEndsAtTypeByteCf()
    {
        var inline = new byte[] { 0xFF, 0xCF }
            .Concat(Enumerable.Repeat((byte)'a', 206))
            .Append((byte)0)
            .ToArray();
        var payload = Assert.IsType<SeStringSegment.Payload>(Assert.Single(SeStringParser.Parse(inline)));
        Assert.Equal(208, payload.Bytes.Length);

        foreach (var expressionType in new byte[] { 0xD0, 0xD7, 0xDF, 0xEF })
        {
            var segments = SeStringParser.Parse([0xFF, expressionType, (byte)'x', 0]);
            Assert.Equal([0xFF], Assert.IsType<SeStringSegment.Payload>(segments[0]).Bytes);
        }
    }

    [Fact]
    public void Serializer_Length207UsesExtendedPackedInteger()
    {
        var inline = SeStringTree.Serialize(
            [new SeNode.Run([new SeNode.Literal(new string('a', 206))], MarkerByte: 0x10)]);
        var extended = SeStringTree.Serialize(
            [new SeNode.Run([new SeNode.Literal(new string('a', 207))], MarkerByte: 0x10)]);

        Assert.Equal([0xFF, 0xCF], inline[..2]);
        Assert.Equal([0xFF, 0xF0, 0xCF], extended[..3]);
        Assert.Equal(inline, SeStringTree.Serialize(SeStringTree.Parse(inline)));
        Assert.Equal(extended, SeStringTree.Serialize(SeStringTree.Parse(extended)));
    }

    [Fact]
    public void Patcher_NestedRunCrossingBoundaryUsesExtendedPrefixes()
    {
        var vanillaWithTerminator = SeStringTree.Serialize(
        [
            new SeNode.Macro(
                Code: 0x08,
                Children:
                [
                    new SeNode.Run([new SeNode.Literal(new string('a', 184))], MarkerByte: 0xB9),
                    new SeNode.Literal("xxxxxx"),
                ],
                LengthMarker: 0xC1),
        ]);
        var vanilla = vanillaWithTerminator[..^1];
        var source = SeStringTreeTokenizer.TokenizeRawText(vanilla);
        var target = source.Replace(new string('a', 184), new string('b', 214), StringComparison.Ordinal);
        var exd = SyntheticExd.BuildExdRaw([(12514u, [vanilla])], fixedSize: 4);
        var replacements = new Dictionary<uint, IReadOnlyList<StringReplacement>>
        {
            [12514u] = [new StringReplacement(source, target, "Text")],
        };

        var result = ExdPatcher.Patch(
            exd,
            fixedDataSize: 4,
            stringColumnOffsets: [0],
            replacements,
            stringColumnFieldNames: ["Text"]);

        Assert.Equal(1, result.Applied);
        Assert.Empty(result.Missed);

        var patched = ExdRowReader.ReadRawStrings(result.Bytes, 4, [0])
            .Single(row => row.RowId == 12514u)
            .Raw;
        Assert.Equal(target, SeStringTreeTokenizer.TokenizeRawText(patched));
        Assert.True(ContainsSequence(patched, [0x02, 0x08, 0xF0, 0xDF]));
        Assert.True(ContainsSequence(patched, [0xFF, 0xF0, 0xD6]));
        Assert.False(ContainsSequence(patched, [0x02, 0x08, 0xDF]));
        Assert.False(ContainsSequence(patched, [0xFF, 0xD7]));
    }

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            if (bytes.AsSpan(i, sequence.Length).SequenceEqual(sequence))
            {
                return true;
            }
        }

        return false;
    }
}
