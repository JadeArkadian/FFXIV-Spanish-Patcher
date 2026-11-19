using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace FFXIVSpanishPatcher.Plugin;

public interface IPenumbraApiTransport : IDisposable
{
    event Action? Initialized;

    event Action? Disposed;

    event Action<bool>? EnabledChanged;

    event Action<string, bool>? ModDirectoryChanged;

    event Action<string>? ModDeleted;

    event Action<string, string>? ModMoved;

    (int Breaking, int Features) GetApiVersion();

    bool GetEnabledState();

    string GetModDirectory();

    IReadOnlyDictionary<string, string> GetModList();

    int AddMod(string directoryName);

    int ReloadMod(string directoryName);

    int SetEnabled(string directoryName, bool enabled);
}

public interface IPenumbraDirectoryProbe
{
    bool Exists(string path);
}

public interface IPluginDiagnosticLogger
{
    void Information(string message);

    void Warning(string message, Exception? exception = null);
}

/// <summary>Typed Penumbra API v5 client that turns unavailable IPC into recoverable state.</summary>
public sealed class PenumbraIpcClient : IPenumbraIpcClient, IPenumbraModRegistry
{
    public const int RequiredBreakingVersion = 5;
    public const int RequiredFeatureVersion = 15;

    private readonly IPenumbraApiTransport _transport;
    private readonly IPenumbraDirectoryProbe _directoryProbe;
    private readonly IPluginDiagnosticLogger _log;
    private bool _disposed;

    public PenumbraIpcClient(
        IPenumbraApiTransport transport,
        IPenumbraDirectoryProbe directoryProbe,
        IPluginDiagnosticLogger log)
    {
        _transport = transport;
        _directoryProbe = directoryProbe;
        _log = log;
        _transport.Initialized += OnInitialized;
        _transport.Disposed += OnDisposed;
        _transport.EnabledChanged += OnEnabledChanged;
        _transport.ModDirectoryChanged += OnModDirectoryChanged;
        _transport.ModDeleted += OnModDeleted;
        _transport.ModMoved += OnModMoved;
    }

    public event Action<PenumbraAvailability>? AvailabilityChanged;

    public event Action<string>? ModDeleted;

    public event Action<string, string>? ModMoved;

    public Task<PenumbraAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProbeAvailability());
    }

    /// <summary>
    /// Rechecks the synchronous IPC contract. The plugin invokes this from the framework thread as
    /// a fallback when an initialization event is missed or arrives before every endpoint is ready.
    /// </summary>
    public PenumbraAvailability ProbeAvailability() => EvaluateAvailability();

    public IReadOnlyDictionary<string, string> GetModList() => _transport.GetModList();

    public int AddMod(string directoryName) => _transport.AddMod(directoryName);

    public int ReloadMod(string directoryName) => _transport.ReloadMod(directoryName);

    public int SetEnabled(string directoryName, bool enabled) => _transport.SetEnabled(directoryName, enabled);

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _transport.Initialized -= OnInitialized;
        _transport.Disposed -= OnDisposed;
        _transport.EnabledChanged -= OnEnabledChanged;
        _transport.ModDirectoryChanged -= OnModDirectoryChanged;
        _transport.ModDeleted -= OnModDeleted;
        _transport.ModMoved -= OnModMoved;
        _transport.Dispose();
        return ValueTask.CompletedTask;
    }

    private PenumbraAvailability EvaluateAvailability()
    {
        try
        {
            var (breaking, features) = _transport.GetApiVersion();
            if (breaking != RequiredBreakingVersion)
            {
                return new PenumbraAvailability(
                    PenumbraAvailabilityStatus.IncompatibleBreakingVersion,
                    breaking,
                    features,
                    null,
                    $"Penumbra usa API incompatible ({breaking}); se requiere {RequiredBreakingVersion}.");
            }

            if (features < RequiredFeatureVersion)
            {
                return new PenumbraAvailability(
                    PenumbraAvailabilityStatus.InsufficientFeatureVersion,
                    breaking,
                    features,
                    null,
                    $"Penumbra no incluye las funciones necesarias (feature {features}; se requiere {RequiredFeatureVersion}).");
            }

            if (!_transport.GetEnabledState())
            {
                return new PenumbraAvailability(
                    PenumbraAvailabilityStatus.Disabled,
                    breaking,
                    features,
                    null,
                    "Penumbra está desactivado.");
            }

            var directory = _transport.GetModDirectory();
            if (String.IsNullOrWhiteSpace(directory) || !_directoryProbe.Exists(directory))
            {
                return new PenumbraAvailability(
                    PenumbraAvailabilityStatus.InvalidModDirectory,
                    breaking,
                    features,
                    directory,
                    "El directorio de mods de Penumbra no existe o no es válido.");
            }

            return new PenumbraAvailability(
                PenumbraAvailabilityStatus.Ready,
                breaking,
                features,
                directory,
                "Penumbra está disponible.");
        }
        catch (IpcNotReadyError exception)
        {
            _log.Information("Penumbra aún no está disponible; se reintentará la conexión.");
            return new PenumbraAvailability(
                PenumbraAvailabilityStatus.Unavailable,
                0,
                0,
                null,
                "Penumbra todavía no está disponible; puede no estar instalado o seguir iniciándose.",
                exception);
        }
        catch (Exception exception)
        {
            _log.Warning("La consulta a Penumbra falló; puede reintentarse.", exception);
            return new PenumbraAvailability(
                PenumbraAvailabilityStatus.Faulted,
                0,
                0,
                null,
                "No se pudo consultar Penumbra. Consulta el diagnóstico y reintenta.",
                exception);
        }
    }

    private void OnInitialized() => Publish("Penumbra se ha inicializado; disponibilidad actualizada.");

    private void OnDisposed()
    {
        if (_disposed)
        {
            return;
        }

        var availability = new PenumbraAvailability(
            PenumbraAvailabilityStatus.Unavailable,
            0,
            0,
            null,
            "Penumbra se ha descargado; el gestor queda en espera.");
        _log.Information(availability.UserMessage);
        AvailabilityChanged?.Invoke(availability);
    }

    private void OnEnabledChanged(bool _) => Publish("El estado de Penumbra ha cambiado; disponibilidad actualizada.");

    private void OnModDirectoryChanged(string _, bool __) => Publish("El directorio de mods de Penumbra ha cambiado; disponibilidad actualizada.");

    private void OnModDeleted(string directoryName)
    {
        if (!_disposed)
        {
            ModDeleted?.Invoke(directoryName);
        }
    }

    private void OnModMoved(string oldDirectoryName, string newDirectoryName)
    {
        if (!_disposed)
        {
            ModMoved?.Invoke(oldDirectoryName, newDirectoryName);
        }
    }

    private void Publish(string message)
    {
        if (_disposed)
        {
            return;
        }

        _log.Information(message);
        AvailabilityChanged?.Invoke(EvaluateAvailability());
    }
}

internal sealed class DalamudPenumbraApiTransport : IPenumbraApiTransport
{
    private const string ApiVersionLabel = "Penumbra.ApiVersion.V5";
    private const string ModDirectoryLabel = "Penumbra.GetModDirectory";
    private const string EnabledStateLabel = "Penumbra.GetEnabledState";
    private readonly ICallGateSubscriber<(int Breaking, int Features)> _apiVersion;
    private readonly ICallGateSubscriber<string> _modDirectory;
    private readonly ICallGateSubscriber<bool> _enabledState;
    private readonly ICallGateSubscriber<Dictionary<string, string>> _modList;
    private readonly ICallGateSubscriber<string, int> _addMod;
    private readonly ICallGateSubscriber<string, string, int> _reloadMod;
    private readonly ICallGateSubscriber<byte, (Guid Id, string Name)?> _defaultCollection;
    private readonly ICallGateSubscriber<Guid, string, string, bool, int> _setMod;
    private readonly ICallGateSubscriber<object?> _initialized;
    private readonly ICallGateSubscriber<object?> _disposed;
    private readonly ICallGateSubscriber<bool, object?> _enabledChanged;
    private readonly ICallGateSubscriber<string, bool, object?> _modDirectoryChanged;
    private readonly ICallGateSubscriber<string, object?> _modDeleted;
    private readonly ICallGateSubscriber<string, string, object?> _modMoved;

    public DalamudPenumbraApiTransport(IDalamudPluginInterface pluginInterface)
    {
        _apiVersion = pluginInterface.GetIpcSubscriber<(int Breaking, int Features)>(ApiVersionLabel);
        _modDirectory = pluginInterface.GetIpcSubscriber<string>(ModDirectoryLabel);
        _enabledState = pluginInterface.GetIpcSubscriber<bool>(EnabledStateLabel);
        _modList = pluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
        _addMod = pluginInterface.GetIpcSubscriber<string, int>("Penumbra.AddMod.V5");
        _reloadMod = pluginInterface.GetIpcSubscriber<string, string, int>("Penumbra.ReloadMod.V5");
        _defaultCollection = pluginInterface.GetIpcSubscriber<byte, (Guid Id, string Name)?>("Penumbra.GetCollection");
        _setMod = pluginInterface.GetIpcSubscriber<Guid, string, string, bool, int>("Penumbra.TrySetMod.V5");
        _initialized = pluginInterface.GetIpcSubscriber<object?>("Penumbra.Initialized");
        _disposed = pluginInterface.GetIpcSubscriber<object?>("Penumbra.Disposed");
        _enabledChanged = pluginInterface.GetIpcSubscriber<bool, object?>("Penumbra.EnabledChange");
        _modDirectoryChanged = pluginInterface.GetIpcSubscriber<string, bool, object?>("Penumbra.ModDirectoryChanged");
        _modDeleted = pluginInterface.GetIpcSubscriber<string, object?>("Penumbra.ModDeleted");
        _modMoved = pluginInterface.GetIpcSubscriber<string, string, object?>("Penumbra.ModMoved");
        _initialized.Subscribe(HandleInitialized);
        _disposed.Subscribe(HandleDisposed);
        _enabledChanged.Subscribe(HandleEnabledChanged);
        _modDirectoryChanged.Subscribe(HandleModDirectoryChanged);
        _modDeleted.Subscribe(HandleModDeleted);
        _modMoved.Subscribe(HandleModMoved);
    }

    public event Action? Initialized;

    public event Action? Disposed;

    public event Action<bool>? EnabledChanged;

    public event Action<string, bool>? ModDirectoryChanged;

    public event Action<string>? ModDeleted;

    public event Action<string, string>? ModMoved;

    public (int Breaking, int Features) GetApiVersion() => _apiVersion.InvokeFunc();

    public bool GetEnabledState() => _enabledState.InvokeFunc();

    public string GetModDirectory() => _modDirectory.InvokeFunc();

    public IReadOnlyDictionary<string, string> GetModList() => _modList.InvokeFunc();

    public int AddMod(string directoryName) => _addMod.InvokeFunc(directoryName);

    public int ReloadMod(string directoryName) => _reloadMod.InvokeFunc(directoryName, string.Empty);

    public int SetEnabled(string directoryName, bool enabled)
    {
        var collection = _defaultCollection.InvokeFunc(0xE0);
        return collection is null
            ? -1
            : _setMod.InvokeFunc(collection.Value.Id, directoryName, string.Empty, enabled);
    }

    public void Dispose()
    {
        _initialized.Unsubscribe(HandleInitialized);
        _disposed.Unsubscribe(HandleDisposed);
        _enabledChanged.Unsubscribe(HandleEnabledChanged);
        _modDirectoryChanged.Unsubscribe(HandleModDirectoryChanged);
        _modDeleted.Unsubscribe(HandleModDeleted);
        _modMoved.Unsubscribe(HandleModMoved);
    }

    private void HandleInitialized() => Initialized?.Invoke();

    private void HandleDisposed() => Disposed?.Invoke();

    private void HandleEnabledChanged(bool enabled) => EnabledChanged?.Invoke(enabled);

    private void HandleModDirectoryChanged(string path, bool valid) => ModDirectoryChanged?.Invoke(path, valid);

    private void HandleModDeleted(string directoryName) => ModDeleted?.Invoke(directoryName);

    private void HandleModMoved(string oldDirectoryName, string newDirectoryName)
        => ModMoved?.Invoke(oldDirectoryName, newDirectoryName);
}

/// <summary>Raw v5 category IPC adapter. The event's first argument stays <see cref="object"/>
/// because Penumbra's enum is not redistributed with this plugin; its value is not needed to
/// reconcile the authoritative snapshot.</summary>
internal sealed class DalamudPenumbraCategoryTransport : IPenumbraCategoryTransport
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyOptions
        = new Dictionary<string, IReadOnlyList<string>>();
    private readonly ICallGateSubscriber<byte, (Guid Id, string Name)?> _defaultCollection;
    private readonly ICallGateSubscriber<string, string, IReadOnlyDictionary<string, (string[] Options, int Type)>?> _availableSettings;
    private readonly ICallGateSubscriber<Guid, string, string, bool, (int Result, (bool Enabled, int Priority, Dictionary<string, List<string>> Options, bool Inherited)? Settings)> _currentSettings;
    private readonly ICallGateSubscriber<Guid, string, string, int, (int Result, (bool ForceInherit, bool Enabled, int Priority, Dictionary<string, List<string>> Options)? Settings, string Source)> _temporarySettings;
    private readonly ICallGateSubscriber<Guid, string, string, bool, int> _setMod;
    private readonly ICallGateSubscriber<Guid, string, string, string, IReadOnlyList<string>, int> _setSettings;
    private readonly ICallGateSubscriber<Guid, string, string, int, int> _removeTemporarySettings;
    private readonly ICallGateSubscriber<object, Guid, string, bool, object?> _settingChanged;

    public DalamudPenumbraCategoryTransport(IDalamudPluginInterface pluginInterface)
    {
        _defaultCollection = pluginInterface.GetIpcSubscriber<byte, (Guid Id, string Name)?>("Penumbra.GetCollection");
        _availableSettings = pluginInterface.GetIpcSubscriber<string, string, IReadOnlyDictionary<string, (string[] Options, int Type)>?>("Penumbra.GetAvailableModSettings.V5");
        _currentSettings = pluginInterface.GetIpcSubscriber<Guid, string, string, bool, (int Result, (bool Enabled, int Priority, Dictionary<string, List<string>> Options, bool Inherited)? Settings)>("Penumbra.GetCurrentModSettings.V5");
        _temporarySettings = pluginInterface.GetIpcSubscriber<Guid, string, string, int, (int Result, (bool ForceInherit, bool Enabled, int Priority, Dictionary<string, List<string>> Options)? Settings, string Source)>("Penumbra.QueryTemporaryModSettings.V5");
        _setMod = pluginInterface.GetIpcSubscriber<Guid, string, string, bool, int>("Penumbra.TrySetMod.V5");
        _setSettings = pluginInterface.GetIpcSubscriber<Guid, string, string, string, IReadOnlyList<string>, int>("Penumbra.TrySetModSettings.V5");
        _removeTemporarySettings = pluginInterface.GetIpcSubscriber<Guid, string, string, int, int>("Penumbra.RemoveTemporaryModSettings.V5");
        _settingChanged = pluginInterface.GetIpcSubscriber<object, Guid, string, bool, object?>("Penumbra.ModSettingChanged.V5");
        _settingChanged.Subscribe(HandleSettingChanged);
    }

    public event Action<Guid, string, bool>? ModSettingChanged;

    public PenumbraCollection? GetDefaultCollection()
    {
        var collection = _defaultCollection.InvokeFunc(0xE0);
        return collection is { } value ? new PenumbraCollection(value.Id, value.Name) : null;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? GetAvailableSettings(string modDirectory)
    {
        var available = _availableSettings.InvokeFunc(modDirectory, string.Empty);
        return available?.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Options,
            StringComparer.Ordinal);
    }

    public PenumbraModSettingsRead GetCurrentSettings(Guid collectionId, string modDirectory)
    {
        var (result, settings) = _currentSettings.InvokeFunc(collectionId, modDirectory, string.Empty, false);
        return settings is null
            ? new PenumbraModSettingsRead(result, false, 0, EmptyOptions, false)
            : new PenumbraModSettingsRead(
                result,
                settings.Value.Enabled,
                settings.Value.Priority,
                settings.Value.Options.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal),
                settings.Value.Inherited);
    }

    public PenumbraTemporaryModSettingsRead GetTemporarySettings(Guid collectionId, string modDirectory, int key)
    {
        var (result, settings, source) = _temporarySettings.InvokeFunc(collectionId, modDirectory, string.Empty, key);
        return settings is null
            ? new PenumbraTemporaryModSettingsRead(result, false, false, false, 0, EmptyOptions, source)
            : new PenumbraTemporaryModSettingsRead(
                result,
                true,
                settings.Value.ForceInherit,
                settings.Value.Enabled,
                settings.Value.Priority,
                settings.Value.Options.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal),
                source);
    }

    public int SetSettings(Guid collectionId, string modDirectory, string groupName, IReadOnlyList<string> enabledOptions)
        => _setSettings.InvokeFunc(collectionId, modDirectory, string.Empty, groupName, enabledOptions);

    public int SetEnabled(Guid collectionId, string modDirectory, bool enabled)
        => _setMod.InvokeFunc(collectionId, modDirectory, string.Empty, enabled);

    public int RemoveTemporarySettings(Guid collectionId, string modDirectory, int key)
        => _removeTemporarySettings.InvokeFunc(collectionId, modDirectory, string.Empty, key);

    public void Dispose() => _settingChanged.Unsubscribe(HandleSettingChanged);

    private void HandleSettingChanged(object _, Guid collectionId, string modDirectory, bool inherited)
        => ModSettingChanged?.Invoke(collectionId, modDirectory, inherited);
}

internal sealed class PhysicalPenumbraDirectoryProbe : IPenumbraDirectoryProbe
{
    public static PhysicalPenumbraDirectoryProbe Instance { get; } = new();

    public bool Exists(string path) => Directory.Exists(path);
}

internal sealed class DalamudPluginDiagnosticLogger(IPluginLog log) : IPluginDiagnosticLogger
{
    public void Information(string message) => log.Information("{Message}", message);

    public void Warning(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            log.Warning("{Message}", message);
            return;
        }

        log.Warning("{Message}: {Exception}", message, exception);
    }
}
