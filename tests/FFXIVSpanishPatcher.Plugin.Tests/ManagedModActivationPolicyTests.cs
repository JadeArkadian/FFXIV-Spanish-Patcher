using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class ManagedModActivationPolicyTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(false, false)]
    public void FirstInstallation_IsEnabledRegardlessOfStaleState(bool? current, bool? persisted)
        => Assert.True(ManagedModActivationPolicy.DesiredAfterRegeneration(null, current, persisted));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(null, null, true)]
    public void Regeneration_PrefersPersistedDesiredThenCurrentState(
        bool? current,
        bool? persisted,
        bool expected)
        => Assert.Equal(expected, ManagedModActivationPolicy.DesiredAfterRegeneration(
            Fingerprint(),
            current,
            persisted));

    [Fact]
    public void IsSessionStateApplied_RequiresVerifiedPersistentState()
    {
        var persistentOnly = Snapshot(modEnabled: true, sessionAllowed: true, usesLease: false);
        var temporary = Snapshot(modEnabled: true, sessionAllowed: true, usesLease: true);

        Assert.True(ManagedModActivationPolicy.IsSessionStateApplied(persistentOnly, desiredEnabled: true));
        Assert.False(ManagedModActivationPolicy.IsSessionStateApplied(temporary, desiredEnabled: true));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, null)]
    public void ChangeMadeInPenumbra_IsAdoptedNotReverted(bool penumbraState, bool? persisted)
    {
        var snapshot = Snapshot(modEnabled: penumbraState, sessionAllowed: true, usesLease: false);

        Assert.True(ManagedModActivationPolicy.ShouldAdoptPenumbraState(snapshot, persisted, regenerationRunning: false));
    }

    [Fact]
    public void ChangeMadeInPenumbra_IsIgnoredWhenAlreadyAgreedOrWhileRegenerating()
    {
        var disabled = Snapshot(modEnabled: false, sessionAllowed: true, usesLease: false);
        var unusable = disabled with { Status = CategorySyncStatus.WriteRejected };

        Assert.False(ManagedModActivationPolicy.ShouldAdoptPenumbraState(disabled, persistedState: false, regenerationRunning: false));
        // The installer switches the mod off while it replaces the files; that is not the user's choice.
        Assert.False(ManagedModActivationPolicy.ShouldAdoptPenumbraState(disabled, persistedState: true, regenerationRunning: true));
        Assert.False(ManagedModActivationPolicy.ShouldAdoptPenumbraState(unusable, persistedState: true, regenerationRunning: false));
    }

    [Theory]
    // Nothing owed: Penumbra is the live truth, also for changes made while the manager was closed.
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, null, true)]
    [InlineData(false, null, null, true)]
    // The manager disabled the mod to regenerate and still owes the user's choice.
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, null, false, false)]
    public void Reconciliation_FollowsPenumbraUnlessAnActivationIsOwed(
        bool activationPending,
        bool? persisted,
        bool? penumbraState,
        bool expected)
        => Assert.Equal(expected, ManagedModActivationPolicy.DesiredAtReconciliation(
            activationPending,
            persisted,
            penumbraState));

    private static CategorySyncSnapshot Snapshot(bool modEnabled, bool sessionAllowed, bool usesLease)
        => new(
            CategorySyncStatus.Ready,
            "Default",
            new HashSet<string>(),
            "Sincronizada.",
            modEnabled,
            sessionAllowed,
            usesLease);

    private static BuildFingerprint Fingerprint()
        => new(1, "plugin", "corpus", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);
}
