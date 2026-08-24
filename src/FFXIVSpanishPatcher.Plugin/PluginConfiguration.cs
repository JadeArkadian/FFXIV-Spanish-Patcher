using Dalamud.Configuration;
using Dalamud.Plugin;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Persisted plugin settings. Migrations never discard a successful fingerprint.</summary>
[Serializable]
public sealed class PluginConfiguration : IPluginConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public bool AutoRegenerationEnabled { get; set; } = true;

    /// <summary>
    /// User's desired state. A verified installation persists this choice in Base/Default so
    /// Penumbra can apply translation resources during early game startup.
    /// </summary>
    public bool? ManagedModEnabled { get; set; }

    public BuildFingerprint? LastSuccessfulFingerprint { get; set; }

    public static PluginConfiguration Migrate(PluginConfiguration? savedConfiguration)
    {
        if (savedConfiguration is null)
        {
            return new PluginConfiguration();
        }

        var configuration = savedConfiguration;

        if (configuration.Version > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"La configuración usa versión futura {configuration.Version}; esta versión admite hasta {CurrentVersion}.");
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

        return configuration;
    }
}

/// <summary>Storage boundary; unit tests use configuration instances without touching Dalamud storage.</summary>
public interface IPluginConfigurationStore
{
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
        store.Save(configuration);
        return true;
    }
}

internal sealed class DalamudPluginConfigurationStore(IDalamudPluginInterface pluginInterface) : IPluginConfigurationStore
{
    public PluginConfiguration Load()
    {
        var configuration = PluginConfiguration.Migrate(pluginInterface.GetPluginConfig() as PluginConfiguration);
        pluginInterface.SavePluginConfig(configuration);
        return configuration;
    }

    public void Save(PluginConfiguration configuration) => pluginInterface.SavePluginConfig(configuration);
}
