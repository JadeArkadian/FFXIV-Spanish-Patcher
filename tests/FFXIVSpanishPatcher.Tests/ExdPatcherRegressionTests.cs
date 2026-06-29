using XivSpanish.GameData;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class ExdPatcherRegressionTests
{
    [Fact]
    public void Patch_FieldUnaware_RunAwareSourceFallsBackToTreeTranslate()
    {
        var raw = SeStringTree.Serialize(
        [
            new SeNode.Literal("Hello, "),
            new SeNode.Run([new SeNode.Literal("miss")], MarkerByte: 0x05),
            new SeNode.Literal("!")
        ]);
        var source = SeStringTreeTokenizer.TokenizeRawText(raw);
        var target = source.Replace("miss", "señorita", StringComparison.Ordinal);
        var exd = SyntheticExd.BuildExdRaw([(16u, [raw])], fixedSize: 4);
        var replacements = new Dictionary<uint, IReadOnlyList<StringReplacement>>
        {
            [16u] = [new StringReplacement(source, target, "Message")],
        };

        var result = ExdPatcher.Patch(
            exd,
            fixedDataSize: 4,
            stringColumnOffsets: [0],
            replacements,
            stringColumnFieldNames: ["Unknown"]);

        Assert.Equal(1, result.Applied);
        Assert.Empty(result.Missed);

        var patchedRaw = ExdRowReader.ReadRawStrings(result.Bytes, 4, [0])
            .Single(row => row.RowId == 16u)
            .Raw;
        Assert.Equal(target, SeStringTreeTokenizer.TokenizeRawText(patchedRaw));
    }
}
