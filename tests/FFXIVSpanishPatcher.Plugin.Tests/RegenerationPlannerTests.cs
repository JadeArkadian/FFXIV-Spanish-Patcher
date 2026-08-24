using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class RegenerationPlannerTests
{
    [Fact]
    public void FirstInstallation_RequiresExplicitUserAction()
        => Assert.Equal(RegenerationDecisionKind.FirstInstallation,
            RegenerationPlanner.Decide(
                Fingerprint(),
                previous: null,
                autoRegenerationEnabled: true,
                installationMissing: true).Kind);

    [Fact]
    public void MatchingInputs_AreUpToDate()
    {
        var fingerprint = Fingerprint();
        Assert.Equal(RegenerationDecisionKind.UpToDate,
            RegenerationPlanner.Decide(
                fingerprint,
                fingerprint,
                autoRegenerationEnabled: true,
                installationMissing: false).Kind);
    }

    [Theory]
    [InlineData(true, RegenerationDecisionKind.InstallationMissing, true)]
    [InlineData(false, RegenerationDecisionKind.InstallationMissingAutoRepairDisabled, false)]
    public void MissingPreviousInstallation_IsReportedAndOnlyAutomaticallyRepairedWhenEnabled(
        bool autoRegenerationEnabled,
        RegenerationDecisionKind expected,
        bool requiresBuild)
    {
        var fingerprint = Fingerprint();

        var decision = RegenerationPlanner.Decide(
            fingerprint,
            fingerprint,
            autoRegenerationEnabled,
            installationMissing: true);

        Assert.Equal(expected, decision.Kind);
        Assert.Equal(requiresBuild, decision.RequiresBuild);
    }

    [Fact]
    public void ChangedGameOrCorpus_RegeneratesOnlyWhenEnabled()
    {
        var previous = Fingerprint();
        var changed = previous with { InstalledGameVersion = "new-game", CorpusSha256 = "new-corpus" };

        Assert.Equal(RegenerationDecisionKind.RegenerationRequired,
            RegenerationPlanner.Decide(
                changed,
                previous,
                autoRegenerationEnabled: true,
                installationMissing: false).Kind);
        Assert.Equal(RegenerationDecisionKind.AutoRegenerationDisabled,
            RegenerationPlanner.Decide(
                changed,
                previous,
                autoRegenerationEnabled: false,
                installationMissing: false).Kind);
    }

    [Fact]
    public void ChangedPayloadTimestampOrCategoryChoice_DoesNotRegenerate()
    {
        var previous = Fingerprint();
        var changed = previous with { SuccessfulPayloadId = "other", CompletedAtUtc = DateTimeOffset.UtcNow };

        Assert.Equal(RegenerationDecisionKind.UpToDate,
            RegenerationPlanner.Decide(
                changed,
                previous,
                autoRegenerationEnabled: true,
                installationMissing: false).Kind);
    }

    private static BuildFingerprint Fingerprint()
        => new(1, "0.6.0", "corpus", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);
}
