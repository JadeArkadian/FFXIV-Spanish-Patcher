using Dalamud.Configuration;
using Dalamud.Plugin;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Persisted plugin settings. Migrations never discard a successful fingerprint.</summary>
[Serializable]
public sealed class PluginConfiguration : IPluginConfiguration
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public bool AutoRegenerationEnabled { get; set; } = true;

    /// <summary>
    /// User's desired state. A verified installation persists this choice in Base/Default so
    /// Penumbra can apply translation resources during early game startup.
    /// </summary>
    public bool? ManagedModEnabled { get; set; }

    public BuildFingerprint? LastSuccessfulFingerprint { get; set; }

    /// <summary>Retained for compatibility with configurations written by newer plugin builds.</summary>
    public bool PluginUpdateNotificationsEnabled { get; set; } = true;

    /// <summary>Last plugin version whose update notification was shown.</summary>
    public string? LastNotifiedPluginUpdateVersion { get; set; }

    public static PluginConfiguration Migrate(PluginConfiguration? savedConfiguration)
    {
        if (savedConfiguration is null)
        {
            return new PluginConfiguration();
        }

        var configuration = savedConfiguration;

        if (configuration.Version > CurrentVersion)
        {
            return configuration;
        }

        if (configuration.Version < 0)
        {
            throw new InvalidOperationException($"La configuración tiene versión inválida {configuration.Version}.");
        }

        if (configuration.Version == 0)
        {
            configuration.AutoRegenerationEnabled = true;
            configuration.Version = 1;
        }

        if (configuration.Version == 1)
        {
            configuration.PluginUpdateNotificationsEnabled = true;
            configuration.Version = 2;
        }

        return configuration;
    }
}

/// <summary>Storage boundary; unit tests use configuration instances without touching Dalamud storage.</summary>
public interface IPluginConfigurationStore
{
    bool IsReadOnly { get; }

    PluginConfiguration Load();

    void Save(PluginConfiguration configuration);
}

/// <summary>Persists a fingerprint only after the complete filesystem and Penumbra transaction succeeds.</summary>
public static class BuildFingerprintPersistence
{
    public static bool SaveIfSuccessful(IPluginConfigurationStore store, PluginConfiguration configuration, ModInstallResult result)
    {
        if (!result.Succeeded || result.PersistedFingerprint is null)
        {
            return false;
        }

        configuration.LastSuccessfulFingerprint = result.PersistedFingerprint;
        if (store.IsReadOnly)
        {
            return false;
        }

        store.Save(configuration);
        return true;
    }
}

internal sealed class DalamudPluginConfigurationStore(IDalamudPluginInterface pluginInterface) : IPluginConfigurationStore
{
    private bool _isReadOnly;

    public bool IsReadOnly => _isReadOnly;

    public PluginConfiguration Load()
    {
        var savedConfiguration = pluginInterface.GetPluginConfig() as PluginConfiguration;
        _isReadOnly = savedConfiguration is not null
            && savedConfiguration.Version > PluginConfiguration.CurrentVersion;
        var configuration = PluginConfiguration.Migrate(savedConfiguration);
        if (!_isReadOnly)
        {
            pluginInterface.SavePluginConfig(configuration);
        }

        return configuration;
    }

    public void Save(PluginConfiguration configuration)
    {
        if (!_isReadOnly)
        {
            pluginInterface.SavePluginConfig(configuration);
        }
    }
}
