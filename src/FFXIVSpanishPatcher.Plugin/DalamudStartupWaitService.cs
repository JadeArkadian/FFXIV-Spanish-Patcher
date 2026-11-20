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
    /// <summary>
    /// The guarantee belongs to the game session, not to one plugin instance: once a Boot load
    /// proved it, a later reload in the same process (disable and re-enable, or a plugin update)
    /// keeps it. Without that proof a non-Boot load cannot know how the game was started.
    /// </summary>
    public static bool IsGuaranteedForCurrentSession(
        bool? enabled,
        PluginLoadReason loadReason,
        bool guaranteedEarlierThisSession = false)
        => enabled == true && (loadReason == PluginLoadReason.Boot || guaranteedEarlierThisSession);

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
/// The Dalamud internals the bridge below reaches by name. None of them is public API, so a
/// Dalamud update may rename any; a member that is no longer there is null. Resolving them calls
/// nothing in Dalamud, which lets a test check them against the Dalamud build CI compiles with.
/// </summary>
public sealed record DalamudStartupWaitMembers(
    MethodInfo? ConfigurationGetter,
    PropertyInfo? WaitProperty,
    MethodInfo? SaveConfiguration)
{
    private const BindingFlags AllMembers =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static DalamudStartupWaitMembers Resolve(Assembly dalamudAssembly)
    {
        var types = LoadableTypes(dalamudAssembly);
        var serviceType = types.FirstOrDefault(type => type.Name == "Service`1" && type.IsGenericTypeDefinition);
        var configurationType = types.FirstOrDefault(type => type.Name == "DalamudConfiguration");
        return new(
            serviceType is null || configurationType is null
                ? null
                : serviceType.MakeGenericType(configurationType).GetMethod("Get", AllMembers),
            configurationType?.GetProperty(DalamudStartupSettings.WaitForPluginsPropertyName, AllMembers),
            configurationType?.GetMethod("Save", AllMembers));
    }

    // Outside the game some of Dalamud's own dependencies are absent; the types that did load are enough.
    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>().ToArray();
        }
    }
}

/// <summary>
/// Narrow reflection bridge to Dalamud's live configuration. This mirrors the approach used by
/// Penumbra and never edits dalamudConfig.json while Dalamud owns it.
/// </summary>
internal sealed class DalamudStartupWaitService
{
    private readonly DalamudStartupWaitMembers? _members;
    private readonly object? _configuration;

    public DalamudStartupWaitService(Assembly dalamudAssembly)
    {
        try
        {
            _members = DalamudStartupWaitMembers.Resolve(dalamudAssembly);
            _configuration = _members.ConfigurationGetter?.Invoke(null, null);
        }
        catch
        {
            _members = null;
            _configuration = null;
        }
    }

    public bool TryRead(out bool enabled)
    {
        enabled = false;
        try
        {
            if (_configuration is null || _members?.WaitProperty?.GetValue(_configuration) is not bool value)
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
            if (_configuration is null
                || _members is not { WaitProperty: { } waitProperty, SaveConfiguration: { } saveConfiguration })
            {
                return false;
            }

            waitProperty.SetValue(_configuration, true);
            saveConfiguration.Invoke(_configuration, null);
            return TryRead(out var enabled) && enabled;
        }
        catch
        {
            return false;
        }
    }
}
