using XivSpanish.GameData;
using XivSpanish.Packager;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class BroadcastPlannerTests
{
    // Run-aware corpus tokenization of Addon row 111 ("Return to ...Aetheryte...?").
    private const string PayloadSource =
        "Return to <EnNoun><Run>PlaceName<RunEnd><Run#2><Sheet><Run#3>Aetheryte<RunEnd#2><Raw><Raw#2>\t<MacroEnd><RunEnd#3><Raw#3><MacroEnd#2>?";

    private const string PayloadTarget =
        "¿Volver a <EnNoun><Run>PlaceName<RunEnd><Run#2><Sheet><Run#3>Aetheryte<RunEnd#2><Raw><Raw#2>\t<MacroEnd><RunEnd#3><Raw#3><MacroEnd#2>?";

    // What the packager reads back from the base EXD: the FLAT tokenizer collapses the run/macro
    // structure, so this never string-equals the run-aware corpus source above. The byte-identity
    // broadcast must not depend on these two comparing equal.
    private const string FlatSource = "Return to <EnNoun><Raw><Payload03>?";

    [Fact]
    public void PlanPayloadSiblings_BroadcastsToByteIdenticalDuplicate_DespiteTokenizationDivergence()
    {
        // Row 111 is the translated representative; row 196 is its byte-identical twin (same
        // RawHash) that the corpus dedup collapsed away, so it is absent from the manifest. Before
        // the fix the source-string join dropped it (the flat base source never matches the
        // run-aware corpus source) and it shipped in vanilla English.
        var columns = new[]
        {
            new BroadcastColumn(111, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
            new BroadcastColumn(196, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
        };

        var decisions = BroadcastPlanner.PlanPayloadSiblings(
            columns,
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [111] = [new StringReplacement(PayloadSource, PayloadTarget, "Text")],
            });

        var decision = Assert.Single(decisions);
        Assert.Equal(196u, decision.RowId);
        // The reviewed corpus source/target are reused verbatim — never re-derived from FlatSource.
        Assert.Equal(PayloadSource, decision.Replacement.Source);
        Assert.Equal(PayloadTarget, decision.Replacement.Target);
        Assert.Equal("Text", decision.Replacement.Field);
    }

    [Fact]
    public void PlanPayloadSiblings_DoesNotBroadcast_WhenRawBytesDiffer()
    {
        var columns = new[]
        {
            new BroadcastColumn(111, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
            new BroadcastColumn(196, "Text", FlatSource, HasPayload: true, RawHash: "RAW-B"),
        };

        var decisions = BroadcastPlanner.PlanPayloadSiblings(
            columns,
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [111] = [new StringReplacement(PayloadSource, PayloadTarget, "Text")],
            });

        Assert.Empty(decisions);
    }

    [Fact]
    public void PlanPayloadSiblings_SkipsRepresentativeRowItself()
    {
        var columns = new[]
        {
            new BroadcastColumn(111, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
        };

        var decisions = BroadcastPlanner.PlanPayloadSiblings(
            columns,
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [111] = [new StringReplacement(PayloadSource, PayloadTarget, "Text")],
            });

        Assert.Empty(decisions);
    }

    [Fact]
    public void PlanPayloadSiblings_SkipsWhenTargetNotSeStringCompatible()
    {
        // A target that drops the payload structure must never be broadcast onto a duplicate.
        const string brokenTarget = "Volver al aeterito?";
        var columns = new[]
        {
            new BroadcastColumn(111, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
            new BroadcastColumn(196, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
        };

        var decisions = BroadcastPlanner.PlanPayloadSiblings(
            columns,
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [111] = [new StringReplacement(PayloadSource, brokenTarget, "Text")],
            });

        Assert.Empty(decisions);
    }

    [Fact]
    public void PlanPayloadSiblings_DisablesBroadcast_WhenByteIdenticalRowsDisagreeOnTarget()
    {
        // Two explicitly-translated rows share field + raw bytes but map to different targets:
        // the broadcast for that signature is ambiguous and must be disabled entirely.
        var columns = new[]
        {
            new BroadcastColumn(111, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
            new BroadcastColumn(120, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
            new BroadcastColumn(196, "Text", FlatSource, HasPayload: true, RawHash: "RAW-A"),
        };

        var decisions = BroadcastPlanner.PlanPayloadSiblings(
            columns,
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [111] = [new StringReplacement(PayloadSource, PayloadTarget, "Text")],
                [120] = [new StringReplacement(PayloadSource, PayloadTarget + " ", "Text")],
            });

        Assert.Empty(decisions);
    }

    [Fact]
    public void Decide_ReturnsNull_ForPayloadColumns()
    {
        // Payload columns are owned by PlanPayloadSiblings; the source-string catalog never
        // broadcasts them (its key is the run-aware corpus source, not the flat base source).
        var catalog = new BroadcastCatalog();
        catalog.Add("Item", "Description", PayloadSource, PayloadTarget);
        var column = new BroadcastColumn(5730, "Description", PayloadSource, HasPayload: true, RawHash: "RAW-A");

        var decision = BroadcastPlanner.Decide(catalog, "Item", column);

        Assert.Null(decision);
    }

    [Fact]
    public void Decide_KeepsAnyFieldFallbackForPlainTextRows()
    {
        var catalog = new BroadcastCatalog();
        catalog.Add("Addon", string.Empty, "Healing Magic Potency", "Potencia de magia curativa");
        var column = new BroadcastColumn(3256, "Text", "Healing Magic Potency", HasPayload: false, RawHash: "RAW-A");

        var decision = BroadcastPlanner.Decide(catalog, "Addon", column);

        Assert.NotNull(decision);
        Assert.Equal(BroadcastKind.Plain, decision!.Kind);
        Assert.Null(decision.ReplacementField);
        Assert.Equal("Potencia de magia curativa", decision.Target);
    }

    [Fact]
    public void Decide_BlocksAmbiguousSources()
    {
        var catalog = new BroadcastCatalog();
        catalog.Add("ENpcResident", "Title", "Gatekeep", "Guarda");
        catalog.Add("ENpcResident", "Title", "Gatekeep", "Portero");
        var column = new BroadcastColumn(100, "Title", "Gatekeep", HasPayload: false, RawHash: "RAW-A");

        var decision = BroadcastPlanner.Decide(catalog, "ENpcResident", column);

        Assert.Null(decision);
    }

    [Fact]
    public void FieldAlias_AddsItemNameFromSameRowSingular_WhenVanillaTextMatches()
    {
        var decisions = FieldAliasPlanner.Decide(
            "Item",
            [
                new BroadcastColumn(9553, "Singular", "Shiva's Diamond Bow", HasPayload: false, RawHash: "RAW-A"),
                new BroadcastColumn(9553, "Name", "Shiva's Diamond Bow", HasPayload: false, RawHash: "RAW-A"),
            ],
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [9553] = [new StringReplacement("Shiva's Diamond Bow", "Arco de diamante de Shiva", "Singular")],
            });

        var decision = Assert.Single(decisions);
        Assert.Equal(9553u, decision.RowId);
        Assert.Equal("Shiva's Diamond Bow", decision.Source);
        Assert.Equal("Arco de diamante de Shiva", decision.Target);
        Assert.Equal("Name", decision.ReplacementField);
    }

    [Fact]
    public void FieldAlias_LeavesExplicitItemNameReplacementAlone()
    {
        var decisions = FieldAliasPlanner.Decide(
            "Item",
            [
                new BroadcastColumn(9553, "Singular", "Shiva's Diamond Bow", HasPayload: false, RawHash: "RAW-A"),
                new BroadcastColumn(9553, "Name", "Shiva's Diamond Bow", HasPayload: false, RawHash: "RAW-A"),
            ],
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [9553] =
                [
                    new StringReplacement("Shiva's Diamond Bow", "Arco de diamante de Shiva", "Singular"),
                    new StringReplacement("Shiva's Diamond Bow", "Arco diamantino de Shiva", "Name"),
                ],
            });

        Assert.Empty(decisions);
    }

    [Fact]
    public void FieldAlias_DoesNotCrossWhenItemNameDiffers()
    {
        var decisions = FieldAliasPlanner.Decide(
            "Item",
            [
                new BroadcastColumn(9553, "Singular", "Shiva's Diamond Bow", HasPayload: false, RawHash: "RAW-A"),
                new BroadcastColumn(9553, "Name", "Different Display Name", HasPayload: false, RawHash: "RAW-B"),
            ],
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [9553] = [new StringReplacement("Shiva's Diamond Bow", "Arco de diamante de Shiva", "Singular")],
            });

        Assert.Empty(decisions);
    }

    [Fact]
    public void FieldAlias_IsCaseSensitive()
    {
        var decisions = FieldAliasPlanner.Decide(
            "Item",
            [
                new BroadcastColumn(3418, "Singular", "Thormoen's subligar", HasPayload: false, RawHash: "RAW-A"),
                new BroadcastColumn(3418, "Name", "Thormoen's Subligar", HasPayload: false, RawHash: "RAW-B"),
            ],
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [3418] = [new StringReplacement("Thormoen's subligar", "subligáculo de Thormoen", "Singular")],
            });

        Assert.Empty(decisions);
    }

    [Fact]
    public void FieldAlias_DoesNotApplyToPayloadColumns()
    {
        var decisions = FieldAliasPlanner.Decide(
            "Item",
            [
                new BroadcastColumn(5729, "Singular", "Plain<NewLine>Payload", HasPayload: true, RawHash: "RAW-A"),
                new BroadcastColumn(5729, "Name", "Plain<NewLine>Payload", HasPayload: true, RawHash: "RAW-A"),
            ],
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [5729] = [new StringReplacement("Plain<NewLine>Payload", "Plano<NewLine>Payload", "Singular")],
            });

        Assert.Empty(decisions);
    }
}
