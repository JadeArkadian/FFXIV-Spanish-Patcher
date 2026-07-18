using XivSpanish.GameData;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class ExdPatcherRegressionTests
{
    [Fact]
    public void Patch_PlainQuestDialogue_CreatesPlayerGenderConditional()
    {
        const string source = "Could it be!? The Warrior of Darkness!";
        const string target = "¡<Gender>Guerrera de la Oscuridad<GenderElse>Guerrero de la Oscuridad<GenderEnd>!";
        var exd = SyntheticExd.BuildExd([(121u, source)]);
        var replacements = new Dictionary<uint, IReadOnlyList<StringReplacement>>
        {
            [121u] = [new StringReplacement(source, target, "Column1")],
        };

        var result = ExdPatcher.Patch(
            exd,
            fixedDataSize: 4,
            stringColumnOffsets: [0],
            replacements,
            stringColumnFieldNames: ["Column1"]);

        Assert.Equal(1, result.Applied);
        Assert.Empty(result.Missed);

        var patchedRaw = ExdRowReader.ReadRawStrings(result.Bytes, 4, [0])
            .Single(row => row.RowId == 121u)
            .Raw;
        Assert.Equal(
            "¡<If><Raw>\u0005<Run>Guerrera de la Oscuridad<RunEnd><Run#2>Guerrero de la Oscuridad<RunEnd#2><MacroEnd>!",
            SeStringTreeTokenizer.TokenizeRawText(patchedRaw));
    }

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
