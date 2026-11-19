using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class ManagedModHeaderStatusTests
{
    [Fact]
    public void ValidatedVersionMismatch_IsActiveBestEffort()
    {
        var snapshot = Create(
            categories: Categories(enabled: true, activationAllowed: true, usesLease: false),
            fingerprint: Fingerprint(installed: "new", recommended: "old"));

        Assert.Equal(ManagedModHeaderStatus.BestEffort, snapshot.Status);
        Assert.Equal("~ ACTIVA · BEST EFFORT", snapshot.Label);
    }

    [Fact]
    public void VersionMismatchWithoutValidatedPersistentState_IsBlocked()
    {
        var snapshot = Create(
            categories: Categories(enabled: false, activationAllowed: false, usesLease: false),
            fingerprint: Fingerprint(installed: "new", recommended: "old"));

        Assert.Equal(ManagedModHeaderStatus.Blocked, snapshot.Status);
    }

    [Fact]
    public void UserChoiceDisabled_WinsAfterSuccessfulValidation()
    {
        var snapshot = Create(
            categories: Categories(enabled: false, activationAllowed: true, usesLease: false),
            configuration: new PluginConfiguration
            {
                ManagedModEnabled = false,
                LastSuccessfulFingerprint = Fingerprint(),
            });

        Assert.Equal(ManagedModHeaderStatus.Disabled, snapshot.Status);
    }

    [Fact]
    public void RegenerationAndRestartHaveExplicitPriority()
    {
        var regenerating = Create(state: State(PluginPhase.Generating));
        var restart = Create(
            state: State(PluginPhase.Generating),
            startupWait: new DalamudStartupWaitSnapshot(DalamudStartupWaitStatus.RestartRequired, "Reinicia."));

        Assert.Equal(ManagedModHeaderStatus.Regenerating, regenerating.Status);
        Assert.Equal(ManagedModHeaderStatus.RestartRequired, restart.Status);
    }

    [Fact]
    public void PendingTranslationRestart_NeverHidesMissingOrRunningInstallation()
    {
        var installed = Create(translationRestartRequired: true);
        var missing = Create(translationRestartRequired: true, installationMissing: true);
        var running = Create(state: State(PluginPhase.Generating), translationRestartRequired: true);

        Assert.Equal(ManagedModHeaderStatus.RestartRequired, installed.Status);
        Assert.Equal(ManagedModHeaderStatus.NotInstalled, missing.Status);
        Assert.Equal(ManagedModHeaderStatus.Regenerating, running.Status);
    }

    private static ManagedModHeaderSnapshot Create(
        PluginStateSnapshot? state = null,
        DalamudStartupWaitSnapshot? startupWait = null,
        CategorySyncSnapshot? categories = null,
        PluginConfiguration? configuration = null,
        BuildFingerprint? fingerprint = null,
        bool installationMissing = false,
        bool translationRestartRequired = false)
    {
        fingerprint ??= Fingerprint();
        configuration ??= new PluginConfiguration
        {
            ManagedModEnabled = true,
            LastSuccessfulFingerprint = fingerprint,
        };
        return ManagedModHeaderStatusModel.Create(
            state ?? State(PluginPhase.Ready),
            startupWait ?? new DalamudStartupWaitSnapshot(DalamudStartupWaitStatus.Ready, "Listo."),
            new PenumbraAvailability(PenumbraAvailabilityStatus.Ready, 5, 15, "/mods", "Listo."),
            categories ?? Categories(enabled: true, activationAllowed: true, usesLease: false),
            new RegenerationDecision(RegenerationDecisionKind.UpToDate, "Listo."),
            configuration,
            fingerprint,
            installationMissing,
            translationRestartRequired);
    }

    private static PluginStateSnapshot State(PluginPhase phase)
        => new(phase, Guid.NewGuid(), "Estado.", null);

    private static CategorySyncSnapshot Categories(bool enabled, bool activationAllowed, bool usesLease)
        => new(CategorySyncStatus.Ready, "Default", new HashSet<string>(), "Categorías.", enabled, activationAllowed, usesLease);

    private static BuildFingerprint Fingerprint(string installed = "same", string recommended = "same")
        => new(1, "plugin", "corpus", recommended, installed, 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);
}
