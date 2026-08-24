using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class TranslationRestartTrackerTests
{
    [Fact]
    public void ActivationChangedThenRestored_ClearsRestartRequirement()
    {
        var tracker = new TranslationRestartTracker();
        tracker.Observe(Snapshot(enabled: true, ["misiones", "interfaz"]));

        tracker.Observe(Snapshot(enabled: false, ["misiones", "interfaz"]));
        Assert.True(tracker.IsRestartRequired);

        tracker.Observe(Snapshot(enabled: true, ["interfaz", "misiones"]));
        Assert.False(tracker.IsRestartRequired);
    }

    [Fact]
    public void CategoriesChangedThenRestored_ClearsRestartRequirement()
    {
        var tracker = new TranslationRestartTracker();
        tracker.Observe(Snapshot(enabled: true, ["misiones", "interfaz"]));

        tracker.Observe(Snapshot(enabled: true, ["misiones"]));
        Assert.True(tracker.IsRestartRequired);

        tracker.Observe(Snapshot(enabled: true, ["misiones", "interfaz"]));
        Assert.False(tracker.IsRestartRequired);
    }

    [Fact]
    public void SuccessfulRebuild_RemainsPendingEvenWhenSettingsMatch()
    {
        var tracker = new TranslationRestartTracker();
        tracker.Observe(Snapshot(enabled: true, ["misiones"]));

        tracker.MarkFilesRebuilt();
        tracker.Observe(Snapshot(enabled: true, ["misiones"]));

        Assert.True(tracker.IsRestartRequired);
    }

    [Fact]
    public void UnusableSnapshots_DoNotBecomeBaseline()
    {
        var tracker = new TranslationRestartTracker();
        tracker.Observe(new CategorySyncSnapshot(
            CategorySyncStatus.ModUnavailable,
            "Default",
            new HashSet<string>(),
            "No disponible."));

        Assert.False(tracker.IsRestartRequired);
        tracker.Observe(Snapshot(enabled: false, ["interfaz"]));
        Assert.False(tracker.IsRestartRequired);
    }

    private static CategorySyncSnapshot Snapshot(bool enabled, string[] domains)
        => new(
            CategorySyncStatus.Ready,
            "Default",
            domains.ToHashSet(StringComparer.OrdinalIgnoreCase),
            "Sincronizada.",
            enabled,
            SessionActivationAllowed: true);
}
