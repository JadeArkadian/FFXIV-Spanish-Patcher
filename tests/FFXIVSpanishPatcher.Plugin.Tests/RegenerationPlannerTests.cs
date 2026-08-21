using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class RegenerationPlannerTests
{
    [Fact]
    public void FirstInstallation_RequiresExplicitUserAction()
        => Assert.Equal(RegenerationDecisionKind.FirstInstallation,
            RegenerationPlanner.Decide(Fingerprint(), null, autoRegenerationEnabled: true).Kind);

    [Fact]
    public void MatchingInputs_AreUpToDate()
    {
        var fingerprint = Fingerprint();
        Assert.Equal(RegenerationDecisionKind.UpToDate,
            RegenerationPlanner.Decide(fingerprint, fingerprint, autoRegenerationEnabled: true).Kind);
    }

    [Fact]
    public void ChangedGameOrCorpus_RegeneratesOnlyWhenEnabled()
    {
        var previous = Fingerprint();
        var changed = previous with { InstalledGameVersion = "new-game", CorpusSha256 = "new-corpus" };

        Assert.Equal(RegenerationDecisionKind.RegenerationRequired,
            RegenerationPlanner.Decide(changed, previous, autoRegenerationEnabled: true).Kind);
        Assert.Equal(RegenerationDecisionKind.AutoRegenerationDisabled,
            RegenerationPlanner.Decide(changed, previous, autoRegenerationEnabled: false).Kind);
    }

    [Fact]
    public void ChangedPayloadTimestampOrCategoryChoice_DoesNotRegenerate()
    {
        var previous = Fingerprint();
        var changed = previous with { SuccessfulPayloadId = "other", CompletedAtUtc = DateTimeOffset.UtcNow };

        Assert.Equal(RegenerationDecisionKind.UpToDate,
            RegenerationPlanner.Decide(changed, previous, autoRegenerationEnabled: true).Kind);
    }

    private static BuildFingerprint Fingerprint()
        => new(1, "0.6.0", "corpus", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);
}
