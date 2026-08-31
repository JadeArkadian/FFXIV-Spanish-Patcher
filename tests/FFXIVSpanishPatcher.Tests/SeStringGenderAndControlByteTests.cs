using XivSpanish.GameData;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

/// <summary>
/// Ported from upstream FFXIV-Spanish (21db9c62e control-byte multiset diagnostics, eca5c2789
/// target-owned gender conditional alongside source payloads). The string literals carry raw
/// control bytes on purpose - do not "clean up" this file by hand.
/// </summary>
public sealed class SeStringGenderAndControlByteTests
{
    [Fact]
    public void LostControlBytes_Fails_NamingEveryLostByte()
    {
        // Doma-style gender fork whose target drops the \x05 parameter byte and the \x0c/\x12
        // expression bytes carried between tokens.
        var source = "Hello, <If><Raw><Run>miss<RunEnd><Run#2>sir<RunEnd#2><MacroEnd>!";
        var target = "Hola, <If><Raw><Run>señorita<RunEnd><Run#2>señor<RunEnd#2><MacroEnd>!";

        var report = SeStringCompatibilityValidator.Validate(source, target);

        Assert.False(report.IsCompatible);
        var violation = Assert.Single(report.Violations);
        Assert.Equal(SeStringViolationKind.ControlCharMismatch, violation.Kind);
        Assert.Contains("lost from target", violation.Message, System.StringComparison.Ordinal);
        Assert.Contains("0x05", violation.Message, System.StringComparison.Ordinal);
        Assert.Contains("0x0C", violation.Message, System.StringComparison.Ordinal);
        Assert.Contains("0x12", violation.Message, System.StringComparison.Ordinal);
        Assert.DoesNotContain("invented in target", violation.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InventedControlByte_Fails_NamingTheExtraByte()
    {
        var source = "A<If>xy<MacroEnd>B";
        var target = "A<If>xy<MacroEnd>B";

        var report = SeStringCompatibilityValidator.Validate(source, target);

        Assert.False(report.IsCompatible);
        var violation = Assert.Single(report.Violations);
        Assert.Equal(SeStringViolationKind.ControlCharMismatch, violation.Kind);
        Assert.Contains("invented in target", violation.Message, System.StringComparison.Ordinal);
        Assert.Contains("0x03", violation.Message, System.StringComparison.Ordinal);
        Assert.DoesNotContain("lost from target", violation.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void SwappedControlBytes_SameMultiset_Fails_AsReordered()
    {
        var source = "ABC<Num>";
        var target = "ABC<Num>";

        var report = SeStringCompatibilityValidator.Validate(source, target);

        Assert.False(report.IsCompatible);
        var violation = Assert.Single(report.Violations);
        Assert.Equal(SeStringViolationKind.ControlCharMismatch, violation.Kind);
        Assert.Contains("reordered", violation.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void PreservedControlBytes_MultisetAndOrder_Pass()
    {
        var text = "Hi <If><Raw><Run>a<RunEnd><Run#2>b<RunEnd#2><MacroEnd>";
        var target = "Eo <If><Raw><Run>x<RunEnd><Run#2>y<RunEnd#2><MacroEnd>";

        var report = SeStringCompatibilityValidator.Validate(text, target);
        Assert.True(report.IsCompatible, report.Describe());
    }

    [Fact]
    public void StandardGenderConditional_OnPayloadSource_IsAllowed()
    {
        var report = SeStringCompatibilityValidator.Validate(
            "Welcome, <Num>",
            "Hola, <Num> <Gender>Guerrera<GenderElse>Guerrero<GenderEnd>");

        Assert.True(report.IsCompatible);
    }

    [Fact]
    public void StandardGenderConditional_OnPayloadSource_StillChecksPayloadMultiset()
    {
        var report = SeStringCompatibilityValidator.Validate(
            "Welcome, <Num>",
            "Hola, <Gender>Guerrera<GenderElse>Guerrero<GenderEnd>");

        Assert.False(report.IsCompatible);
        Assert.Contains(report.Violations, v => v.Kind == SeStringViolationKind.MissingPayload);
    }

    [Fact]
    public void StandardGenderConditional_OnPayloadSource_StillChecksPayloadOrder()
    {
        var report = SeStringCompatibilityValidator.Validate(
            "<Num> then <Num#2>",
            "<Num#2> luego <Num> <Gender>Guerrera<GenderElse>Guerrero<GenderEnd>");

        Assert.False(report.IsCompatible);
        Assert.Contains(report.Violations, v => v.Kind != SeStringViolationKind.InvalidStandardMacro);
    }

    [Fact]
    public void StandardGenderConditional_OnPayloadSource_RejectsPayloadInsideBranch()
    {
        var report = SeStringCompatibilityValidator.Validate(
            "Welcome, <Num>",
            "Hola, <Gender><Num><GenderElse>Guerrero<GenderEnd>");

        Assert.False(report.IsCompatible);
        Assert.Equal(SeStringViolationKind.InvalidStandardMacro, Assert.Single(report.Violations).Kind);
    }

    [Fact]
    public void TryTranslate_PayloadSource_AllowsGenderConditionalAlongsideSourcePayloads()
    {
        // Vanilla carries a real payload of its own (an If macro), like the Anogg letters whose
        // source splits the player's name. The target keeps that payload AND adds a target-owned
        // gender conditional, which the source never had.
        var vanilla = SeStringTree.Serialize(
        [
            new SeNode.Literal("Dear "),
            new SeNode.Macro(
                0x08,
                [
                    new SeNode.RawByte(0xE9),
                    new SeNode.Literal("\u0005"),
                    new SeNode.Run([new SeNode.Literal("friend")], 0x01),
                    new SeNode.Run([new SeNode.Literal("friend")], 0x01),
                ],
                0x01),
            new SeNode.Literal(","),
        ]);

        var source = SeStringTreeTokenizer.TokenizeRawText(vanilla);
        var payloadStart = source.IndexOf('<');
        var payloadEnd = source.LastIndexOf('>') + 1;
        var sourcePayload = source.Substring(payloadStart, payloadEnd - payloadStart);
        var target = "<Gender>Querida<GenderElse>Querido<GenderEnd> " + sourcePayload + ":";

        var translated = SeStringTree.TryTranslate(vanilla, source, target, out var result, out var reason);

        Assert.True(translated, reason);

        var rebuilt = SeStringTree.Parse(result);

        // The target-owned conditional is emitted with the official French binary framing...
        var gender = Assert.IsType<SeNode.Macro>(rebuilt[0]);
        Assert.Equal(
            "020815E905FF0851756572696461FF085175657269646F03",
            Convert.ToHexString(SeStringTree.SerializeNode(gender)));

        // ...and the source's own payload survives untouched.
        var carried = Assert.IsType<SeNode.Macro>(rebuilt[2]);
        Assert.Equal(0x08, carried.Code);
        Assert.Equal("friend", Assert.IsType<SeNode.Literal>(Assert.IsType<SeNode.Run>(carried.Children[2]).Children[0]).Text);
    }

    [Fact]
    public void TryTranslate_PayloadSource_RejectsGenderConditionalThatDropsASourcePayload()
    {
        var vanilla = SeStringTree.Serialize(
        [
            new SeNode.Literal("Dear "),
            new SeNode.Macro(
                0x08,
                [
                    new SeNode.RawByte(0xE9),
                    new SeNode.Literal("\u0005"),
                    new SeNode.Run([new SeNode.Literal("friend")], 0x01),
                    new SeNode.Run([new SeNode.Literal("friend")], 0x01),
                ],
                0x01),
        ]);

        var source = SeStringTreeTokenizer.TokenizeRawText(vanilla);

        // Dropping the source payload and keeping only the target-owned conditional is caught by
        // the compatibility gate, which is where payload-loss is enforced for every target.
        var report = SeStringCompatibilityValidator.Validate(
            source,
            "<Gender>Querida<GenderElse>Querido<GenderEnd>");

        Assert.False(report.IsCompatible);
        Assert.Contains(report.Violations, v => v.Kind == SeStringViolationKind.MissingPayload);
    }
}
