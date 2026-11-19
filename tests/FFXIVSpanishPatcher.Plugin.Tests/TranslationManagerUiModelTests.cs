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

    [Fact]
    public void CancelledRegeneration_IsNotPresentedAsAppliedWhenARestartWasAlreadyPending()
    {
        var snapshot = Status(PluginPhase.Cancelled, installed: true, missing: false, restartRequired: true);

        Assert.Equal("Operación cancelada. La traducción instalada no ha cambiado.", snapshot.StatusMessage);
        Assert.Equal(StatusTone.Warning, snapshot.StatusTone);
        Assert.True(snapshot.CanStartRegeneration);
        Assert.True(snapshot.CanRestartGame);
    }

    [Fact]
    public void CancelledReinstallation_SaysNothingIsInstalledAndOffersNoRestart()
    {
        var snapshot = Status(PluginPhase.Cancelled, installed: true, missing: true, restartRequired: true);

        Assert.Equal("Operación cancelada. La traducción no está instalada.", snapshot.StatusMessage);
        Assert.True(snapshot.CanStartRegeneration);
        Assert.False(snapshot.CanRestartGame);
    }

    [Fact]
    public void FailedRegeneration_ShowsItsErrorAheadOfAPendingRestart()
    {
        var snapshot = Status(PluginPhase.ErrorRecoverable, installed: true, missing: false, restartRequired: true);

        Assert.Equal("Estado.", snapshot.StatusMessage);
        Assert.Equal(StatusTone.Error, snapshot.StatusTone);
        Assert.True(snapshot.CanStartRegeneration);
    }

    [Fact]
    public void CompletedRegeneration_AsksForRestart()
    {
        var snapshot = Status(PluginPhase.Ready, installed: true, missing: false, restartRequired: true);

        Assert.StartsWith("Hay cambios pendientes de aplicar.", snapshot.StatusMessage, StringComparison.Ordinal);
        Assert.True(snapshot.CanRestartGame);
    }

    private static TranslationManagerUiSnapshot Status(
        PluginPhase phase,
        bool installed,
        bool missing,
        bool restartRequired)
        => TranslationManagerUiModel.Create(
            new PluginStateSnapshot(phase, Guid.NewGuid(), "Estado.", null),
            StartupWait(DalamudStartupWaitStatus.Ready),
            new PenumbraAvailability(PenumbraAvailabilityStatus.Ready, 5, 15, "mods", "Listo."),
            Categories(CategorySyncStatus.Ready),
            new RegenerationDecision(RegenerationDecisionKind.UpToDate, "Al día."),
            new PluginConfiguration { LastSuccessfulFingerprint = installed ? Fingerprint() : null },
            Fingerprint(),
            installationMissing: missing,
            translationRestartRequired: restartRequired);

    private static CategorySyncSnapshot Categories(CategorySyncStatus status)
        => new(status, "Default", new HashSet<string>(), "Categorías.");

    private static DalamudStartupWaitSnapshot StartupWait(DalamudStartupWaitStatus status)
        => new(status, "Carga temprana.");

    private static BuildFingerprint Fingerprint()
        => new(1, "0.0.0", "0123456789abcdef0123456789abcdef", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);
}
