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

    [Fact]
    public void NeedsSessionRepair_UpToDatePersistentChoiceChanged_ReturnsTrue()
    {
        var snapshot = Snapshot(modEnabled: false, sessionAllowed: true, usesLease: false);
        var regeneration = new RegenerationDecision(RegenerationDecisionKind.UpToDate, "Actualizada.");

        Assert.True(ManagedModActivationPolicy.NeedsSessionRepair(snapshot, desiredEnabled: true, regeneration));
        Assert.False(ManagedModActivationPolicy.NeedsSessionRepair(snapshot, desiredEnabled: false, regeneration));
    }

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
