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
            StartupWait(DalamudStartupWaitStatus.Ready),
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
            StartupWait(DalamudStartupWaitStatus.Ready),
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

    [Theory]
    [InlineData(DalamudStartupWaitStatus.Disabled)]
    [InlineData(DalamudStartupWaitStatus.RestartRequired)]
    [InlineData(DalamudStartupWaitStatus.Unavailable)]
    public void StartupWaitRequirement_BlocksRegenerationAndCategories(DalamudStartupWaitStatus status)
    {
        var snapshot = TranslationManagerUiModel.Create(
            new PluginStateSnapshot(PluginPhase.Ready, Guid.NewGuid(), "Listo.", null),
            StartupWait(status),
            new PenumbraAvailability(PenumbraAvailabilityStatus.Ready, 5, 1, "mods", "Listo."),
            Categories(CategorySyncStatus.Ready),
            null,
            new PluginConfiguration(),
            Fingerprint());

        Assert.False(snapshot.CanStartRegeneration);
        Assert.False(snapshot.CanEditCategories);
        Assert.Contains($"Carga temprana de Dalamud: {status}", snapshot.Diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RegenerationDecisionKind.InstallationMissing)]
    [InlineData(RegenerationDecisionKind.InstallationMissingAutoRepairDisabled)]
    public void MissingManagedInstallation_OffersReinstall(RegenerationDecisionKind kind)
    {
        var snapshot = TranslationManagerUiModel.Create(
            new PluginStateSnapshot(PluginPhase.Ready, Guid.NewGuid(), "Listo.", null),
            StartupWait(DalamudStartupWaitStatus.Ready),
            new PenumbraAvailability(PenumbraAvailabilityStatus.Ready, 5, 1, "mods", "Listo."),
            Categories(CategorySyncStatus.ModUnavailable),
            new RegenerationDecision(kind, "La instalación ha desaparecido."),
            new PluginConfiguration { LastSuccessfulFingerprint = Fingerprint() },
            Fingerprint());

        Assert.Equal("Reinstalar traducción", snapshot.PrimaryActionLabel);
        Assert.True(snapshot.CanStartRegeneration);
    }

    private static CategorySyncSnapshot Categories(CategorySyncStatus status)
        => new(status, "Default", new HashSet<string>(), "Categorías.");

    private static DalamudStartupWaitSnapshot StartupWait(DalamudStartupWaitStatus status)
        => new(status, "Carga temprana.");

    private static BuildFingerprint Fingerprint()
        => new(1, "0.0.0", "0123456789abcdef0123456789abcdef", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);
}
