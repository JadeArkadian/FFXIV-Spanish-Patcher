using System.Reflection;
using Dalamud.Plugin;
using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

public enum DalamudStartupWaitStatus
{
    Ready,
    Disabled,
    RestartRequired,
    Unavailable,
}

public sealed record DalamudStartupWaitSnapshot(
    DalamudStartupWaitStatus Status,
    string Message)
{
    public bool IsReady => Status == DalamudStartupWaitStatus.Ready;
    public bool CanEnable => Status == DalamudStartupWaitStatus.Disabled;
}

public static class DalamudStartupWaitPolicy
{
    public static bool IsGuaranteedForCurrentSession(bool? enabled, PluginLoadReason loadReason)
        => enabled == true && loadReason == PluginLoadReason.Boot;

    public static DalamudStartupWaitSnapshot Evaluate(bool? enabled, bool guaranteedForCurrentSession)
        => enabled switch
        {
            null => new(
                DalamudStartupWaitStatus.Unavailable,
                "No se ha podido comprobar la carga temprana de Dalamud. Revisa el ajuste en /xlsettings."),
            false => new(
                DalamudStartupWaitStatus.Disabled,
                "Activa Wait for plugins before game loads para utilizar el gestor de traducción."),
            true when guaranteedForCurrentSession => new(
                DalamudStartupWaitStatus.Ready,
                "Dalamud esperó a que los plugins cargaran antes de iniciar el juego."),
            _ => new(
                DalamudStartupWaitStatus.RestartRequired,
                "La carga temprana ya está activada. Reinicia el juego para que tenga efecto."),
        };
}

/// <summary>
/// Narrow reflection bridge to Dalamud's live configuration. This mirrors the approach used by
/// Penumbra and never edits dalamudConfig.json while Dalamud owns it.
/// </summary>
internal sealed class DalamudStartupWaitService
{
    private const BindingFlags AllMembers =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly object? _configuration;
    private readonly PropertyInfo? _waitProperty;
    private readonly MethodInfo? _saveConfiguration;
    private readonly MethodInfo? _interfaceGetter;

    public DalamudStartupWaitService(Assembly dalamudAssembly)
    {
        try
        {
            var serviceType = dalamudAssembly.DefinedTypes
                .FirstOrDefault(type => type.Name == "Service`1" && type.IsGenericTypeDefinition);
            var configurationType = dalamudAssembly.DefinedTypes
                .FirstOrDefault(type => type.Name == "DalamudConfiguration");
            var interfaceType = dalamudAssembly.DefinedTypes
                .FirstOrDefault(type => type.Name == "DalamudInterface");
            if (serviceType is null || configurationType is null || interfaceType is null)
            {
                return;
            }

            var configurationService = serviceType.MakeGenericType(configurationType);
            var interfaceService = serviceType.MakeGenericType(interfaceType);
            var configurationGetter = configurationService.GetMethod("Get", AllMembers);
            _interfaceGetter = interfaceService.GetMethod("Get", AllMembers);
            _configuration = configurationGetter?.Invoke(null, null);
            if (_configuration is null)
            {
                return;
            }

            _waitProperty = _configuration.GetType().GetProperty(
                DalamudStartupSettings.WaitForPluginsPropertyName,
                AllMembers);
            _saveConfiguration = _configuration.GetType().GetMethod("Save", AllMembers);
        }
        catch
        {
            _configuration = null;
            _waitProperty = null;
            _saveConfiguration = null;
            _interfaceGetter = null;
        }
    }

    public bool TryRead(out bool enabled)
    {
        enabled = false;
        try
        {
            if (_configuration is null || _waitProperty?.GetValue(_configuration) is not bool value)
            {
                return false;
            }

            enabled = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryEnable()
    {
        try
        {
            if (_configuration is null || _waitProperty is null || _saveConfiguration is null)
            {
                return false;
            }

            _waitProperty.SetValue(_configuration, true);
            SynchronizeOpenSettingsWindow();
            _saveConfiguration.Invoke(_configuration, null);
            return TryRead(out var enabled) && enabled;
        }
        catch
        {
            return false;
        }
    }

    private void SynchronizeOpenSettingsWindow()
    {
        try
        {
            var dalamudInterface = _interfaceGetter?.Invoke(null, null);
            var settingsWindow = dalamudInterface?.GetType()
                .GetField("settingsWindow", AllMembers)
                ?.GetValue(dalamudInterface);
            settingsWindow?.GetType()
                .GetField("doWaitForPluginsOnStartup", AllMembers)
                ?.SetValue(settingsWindow, true);
        }
        catch
        {
            // The setting itself remains authoritative; UI synchronization is best effort.
        }
    }
}
