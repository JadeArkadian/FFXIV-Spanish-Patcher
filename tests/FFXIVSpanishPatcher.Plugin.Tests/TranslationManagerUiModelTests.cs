using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class TranslationManagerUiModelTests
{
    [Fact]
    public void FirstInstallation_OffersInstallAndDoesNotExposeFullCorpusHash()
    {
        var snapshot = TranslationManagerUiModel.Create(
            new PluginStateSnapshot(PluginPhase.WelcomeRequired, Guid.NewGuid(), "Bienvenida.", null),
            new PenumbraAvailability(PenumbraAvailabilityStatus.Ready, 5, 1, "mods", "Listo."),
            Categories(CategorySyncStatus.ModUnavailable),
            null,
            new PluginConfiguration(),
            Fingerprint());

        Assert.Equal("Instalar traducción", snapshot.PrimaryActionLabel);
        Assert.True(snapshot.CanStartRegeneration);
        Assert.False(snapshot.CanCancelRegeneration);
        Assert.Contains("0123456789ab…", snapshot.Diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef", snapshot.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void RunningOperation_OnlyOffersCancellation()
    {
        var configuration = new PluginConfiguration { LastSuccessfulFingerprint = Fingerprint() };
        var snapshot = TranslationManagerUiModel.Create(
            new PluginStateSnapshot(PluginPhase.Generating, Guid.NewGuid(), "Generando.", null),
            new PenumbraAvailability(PenumbraAvailabilityStatus.Ready, 5, 1, "mods", "Listo."),
            Categories(CategorySyncStatus.Ready),
            null,
            configuration,
            Fingerprint());

        Assert.Equal("Regenerar traducción", snapshot.PrimaryActionLabel);
        Assert.False(snapshot.CanStartRegeneration);
        Assert.True(snapshot.CanCancelRegeneration);
        Assert.False(snapshot.CanEditCategories);
    }

    private static CategorySyncSnapshot Categories(CategorySyncStatus status)
        => new(status, "Default", new HashSet<string>(), "Categorías.");

    private static BuildFingerprint Fingerprint()
        => new(1, "0.0.0", "0123456789abcdef0123456789abcdef", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);
}
