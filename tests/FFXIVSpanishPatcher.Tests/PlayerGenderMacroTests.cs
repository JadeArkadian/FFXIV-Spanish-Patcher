using XivSpanish.GameData;
using XivSpanish.Packager;
using XivSpanish.Translation;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class PlayerGenderMacroTests
{
    [Fact]
    public void TreeTranslate_PlainSource_CreatesOfficialPlayerGenderConditional()
    {
        var vanilla = System.Text.Encoding.UTF8.GetBytes("Welcome, Warrior of Light.");
        const string target = "Te damos la bienvenida, <Gender>Guerrera<GenderElse>Guerrero<GenderEnd> de la Luz.";

        var translated = SeStringTree.TryTranslate(
            vanilla,
            "Welcome, Warrior of Light.",
            target,
            out var result,
            out var reason);

        Assert.True(translated, reason);
        Assert.Equal(
            "Te damos la bienvenida, <If><Raw>\u0005<Run>Guerrera<RunEnd><Run#2>Guerrero<RunEnd#2><MacroEnd> de la Luz.",
            SeStringTreeTokenizer.TokenizeRawText(result));

        var macro = Assert.IsType<SeNode.Macro>(SeStringTree.Parse(result)[1]);
        Assert.Equal("020817E905FF094775657272657261FF09477565727265726F03", Convert.ToHexString(SeStringTree.SerializeNode(macro)));
    }

    [Theory]
    [InlineData("<Gender>Guerrera<GenderEnd>")]
    [InlineData("<Gender><Num><GenderElse>Guerrero<GenderEnd>")]
    [InlineData("<Gender>Guerrera<GenderElse><GenderEnd>")]
    public void ManifestGate_InvalidGenderMacroOnPlainSource_IsUnsafe(string target)
    {
        var entry = Entry("Warrior of Light", target);

        var violation = Assert.Single(ManifestSeStringGate.Check([entry]));

        Assert.Equal(SeStringViolationKind.InvalidStandardMacro, Assert.Single(violation.Report.Violations).Kind);
    }

    [Fact]
    public void ManifestGate_ValidGenderMacroOnPlainSource_Passes()
    {
        var entry = Entry(
            "Warrior of Light",
            "<Gender>Guerrera de la Luz<GenderElse>Guerrero de la Luz<GenderEnd>");

        Assert.Empty(ManifestSeStringGate.Check([entry]));
    }

    [Fact]
    public void Validator_GenderMacroOnPayloadSource_Fails()
    {
        var report = SeStringCompatibilityValidator.Validate(
            "Welcome, <Num>",
            "Hola, <Num> <Gender>Guerrera<GenderElse>Guerrero<GenderEnd>");

        Assert.False(report.IsCompatible);
        Assert.Equal(SeStringViolationKind.InvalidStandardMacro, Assert.Single(report.Violations).Kind);
    }

    private static TranslationEntry Entry(string source, string target) => new()
    {
        Source = source,
        Target = target,
        Status = TranslationEntryStatus.Approved,
        SourceKey = new TranslationSourceKey
        {
            Sheet = "quest/045/Test",
            RowId = 1,
            Field = "Column1",
            ExdPath = "exd/quest/045/test_0_en.exd",
        },
    };
}
