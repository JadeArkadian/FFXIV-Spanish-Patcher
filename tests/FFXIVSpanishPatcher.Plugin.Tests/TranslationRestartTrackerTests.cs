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

    [Fact]
    public void PluginReload_WithUnchangedState_DoesNotRequireRestart()
    {
        var session = new FakeSessionMemory();
        new TranslationRestartTracker(session).Observe(Snapshot(enabled: true, ["misiones", "interfaz"]));

        var reloaded = new TranslationRestartTracker(session);
        reloaded.Observe(Snapshot(enabled: true, ["interfaz", "misiones"]));

        Assert.False(reloaded.IsRestartRequired);
    }

    [Fact]
    public void PluginReload_KeepsARealPendingRestart()
    {
        var rebuilt = new FakeSessionMemory();
        new TranslationRestartTracker(rebuilt).MarkFilesRebuilt();
        Assert.True(new TranslationRestartTracker(rebuilt).IsRestartRequired);

        // The baseline is the state the game booted with, not the state seen after the reload.
        var changed = new FakeSessionMemory();
        var first = new TranslationRestartTracker(changed);
        first.Observe(Snapshot(enabled: true, ["misiones"]));
        first.Observe(Snapshot(enabled: false, ["misiones"]));

        var reloaded = new TranslationRestartTracker(changed);
        reloaded.Observe(Snapshot(enabled: false, ["misiones"]));

        Assert.True(reloaded.IsRestartRequired);
    }

    private sealed class FakeSessionMemory : IGameSessionMemory
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Get(string key) => _values.GetValueOrDefault(key);

        public void Set(string key, string value) => _values[key] = value;
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
