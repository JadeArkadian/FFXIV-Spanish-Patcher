using XivSpanish.GameData;
using XivSpanish.Packager;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class BroadcastPlannerTests
{
    private const string PayloadSource = "Once common.<NewLine>Exchange at salvagers.";
    private const string PayloadTarget = "Antes era comun.<NewLine>Se intercambia en recuperadores.";

    [Fact]
    public void Decide_AllowsPayloadBroadcast_WhenExplicitRowHasSameRawSignature()
    {
        var catalog = new BroadcastCatalog();
        catalog.Add("Item", "Description", PayloadSource, PayloadTarget);
        var columns = new[]
        {
            new BroadcastColumn(5729, "Description", PayloadSource, HasPayload: true, RawHash: "RAW-A"),
            new BroadcastColumn(5730, "Description", PayloadSource, HasPayload: true, RawHash: "RAW-A"),
        };
        var signatures = BroadcastPlanner.BuildPayloadSignatures(
            columns,
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [5729] = [new StringReplacement(PayloadSource, PayloadTarget, "Description")],
            });

        var decision = BroadcastPlanner.Decide(catalog, "Item", columns[1], signatures);

        Assert.NotNull(decision);
        Assert.Equal(BroadcastKind.Payload, decision!.Kind);
        Assert.Equal(PayloadTarget, decision.Target);
        Assert.Equal("Description", decision.ReplacementField);
    }

    [Fact]
    public void Decide_BlocksPayloadBroadcast_WhenRawSignatureDiffers()
    {
        var catalog = new BroadcastCatalog();
        catalog.Add("Item", "Description", PayloadSource, PayloadTarget);
        var columns = new[]
        {
            new BroadcastColumn(5729, "Description", PayloadSource, HasPayload: true, RawHash: "RAW-A"),
            new BroadcastColumn(5730, "Description", PayloadSource, HasPayload: true, RawHash: "RAW-B"),
        };
        var signatures = BroadcastPlanner.BuildPayloadSignatures(
            columns,
            new Dictionary<uint, IReadOnlyList<StringReplacement>>
            {
                [5729] = [new StringReplacement(PayloadSource, PayloadTarget, "Description")],
            });

        var decision = BroadcastPlanner.Decide(catalog, "Item", columns[1], signatures);

        Assert.Null(decision);
    }

    [Fact]
    public void Decide_DoesNotUseAnyFieldFallbackForPayloadRows()
    {
        var catalog = new BroadcastCatalog();
        catalog.Add("Item", string.Empty, PayloadSource, PayloadTarget);
        var column = new BroadcastColumn(5730, "Description", PayloadSource, HasPayload: true, RawHash: "RAW-A");

        var decision = BroadcastPlanner.Decide(
            catalog,
            "Item",
            column,
            new HashSet<PayloadBroadcastSignature>
            {
                new("Description", PayloadSource, PayloadTarget, "RAW-A"),
            });

        Assert.Null(decision);
    }

    [Fact]
    public void Decide_KeepsAnyFieldFallbackForPlainTextRows()
    {
        var catalog = new BroadcastCatalog();
        catalog.Add("Addon", string.Empty, "Healing Magic Potency", "Potencia de magia curativa");
        var column = new BroadcastColumn(3256, "Text", "Healing Magic Potency", HasPayload: false, RawHash: "RAW-A");

        var decision = BroadcastPlanner.Decide(catalog, "Addon", column, new HashSet<PayloadBroadcastSignature>());

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

        var decision = BroadcastPlanner.Decide(catalog, "ENpcResident", column, new HashSet<PayloadBroadcastSignature>());

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
                [3418] = [new StringReplacement("Thormoen's subligar", "subligaculo de Thormoen", "Singular")],
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
