using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void Migrate_NullConfiguration_CreatesCurrentDefaults()
    {
        var configuration = PluginConfiguration.Migrate(null);

        Assert.Equal(PluginConfiguration.CurrentVersion, configuration.Version);
        Assert.True(configuration.AutoRegenerationEnabled);
        Assert.Null(configuration.LastSuccessfulFingerprint);
    }

    [Fact]
    public void Migrate_VersionZero_PreservesFingerprintAndAppliesNewDefault()
    {
        var fingerprint = TestFingerprint();
        var saved = new PluginConfiguration
        {
            Version = 0,
            AutoRegenerationEnabled = false,
            LastSuccessfulFingerprint = fingerprint,
        };

        var configuration = PluginConfiguration.Migrate(saved);

        Assert.Same(saved, configuration);
        Assert.Equal(PluginConfiguration.CurrentVersion, configuration.Version);
        Assert.True(configuration.AutoRegenerationEnabled);
        Assert.Same(fingerprint, configuration.LastSuccessfulFingerprint);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Migrate_UnsupportedVersion_Throws(int version)
    {
        var saved = new PluginConfiguration { Version = version };

        Assert.Throws<InvalidOperationException>(() => PluginConfiguration.Migrate(saved));
    }

    [Fact]
    public void SaveIfSuccessful_PersistsOnlySuccessfulFingerprint()
    {
        var store = new RecordingStore();
        var configuration = new PluginConfiguration();
        var fingerprint = TestFingerprint();

        var saved = BuildFingerprintPersistence.SaveIfSuccessful(
            store,
            configuration,
            new ModInstallResult(true, "ok", "FFXIVSpanish", fingerprint));

        Assert.True(saved);
        Assert.Same(fingerprint, configuration.LastSuccessfulFingerprint);
        Assert.Same(configuration, store.Saved);
    }

    [Fact]
    public void SaveIfSuccessful_FailedInstallation_DoesNotChangeConfiguration()
    {
        var store = new RecordingStore();
        var configuration = new PluginConfiguration { LastSuccessfulFingerprint = TestFingerprint() };

        var saved = BuildFingerprintPersistence.SaveIfSuccessful(
            store,
            configuration,
            new ModInstallResult(false, "fallo"));

        Assert.False(saved);
        Assert.Null(store.Saved);
        Assert.NotNull(configuration.LastSuccessfulFingerprint);
    }

    private static BuildFingerprint TestFingerprint()
        => new(1, "0.6.0", "hash", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);

    private sealed class RecordingStore : IPluginConfigurationStore
    {
        public PluginConfiguration? Saved { get; private set; }
        public PluginConfiguration Load() => new();
        public void Save(PluginConfiguration configuration) => Saved = configuration;
    }
}
