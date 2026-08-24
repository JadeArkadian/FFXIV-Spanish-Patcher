using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

public enum CategorySyncStatus
{
    Ready,
    Inherited,
    DefaultCollectionUnavailable,
    ModUnavailable,
    CategoryGroupMissing,
    CategoryOptionMissing,
    WriteRejected,
    Faulted,
    Disposed,
}

public sealed record CategorySyncSnapshot(
    CategorySyncStatus Status,
    string CollectionName,
    IReadOnlySet<string> EnabledDomains,
    string Message,
    bool? ModEnabled = null,
    bool SessionActivationAllowed = false,
    bool UsesSessionLease = false)
{
    public bool CanEdit => Status is CategorySyncStatus.Ready or CategorySyncStatus.Inherited;
}

/// <summary>
/// Maintains the managed mod state and stable category group in Penumbra's Base/Default collection.
/// It never reads or writes Current, Interface or individual collections, and it reconciles after
/// every write instead of treating the local request as truth.
/// </summary>
public sealed class CategorySyncService : IAsyncDisposable
{
    internal const int SessionLeaseKey = -60421;
    internal const string SessionLeaseSource = "FFXIV Spanish Translation Manager";

    private readonly IPenumbraCategoryTransport _transport;
    private readonly IPluginDiagnosticLogger _log;
    private readonly object _gate = new();
    private long _generation;
    private PendingWrite? _pendingWrite;
    private string? _managedDirectory;
    private bool _sessionActivationAllowed;
    private bool _desiredModEnabled;
    private bool _handlingWrite;
    private bool _disposed;

    public CategorySyncService(IPenumbraCategoryTransport transport, IPluginDiagnosticLogger log)
    {
        _transport = transport;
        _log = log;
        Snapshot = new CategorySyncSnapshot(CategorySyncStatus.ModUnavailable, "Base/Default", new HashSet<string>(),
            "La traducción todavía no está instalada en Penumbra.");
        _transport.ModSettingChanged += OnModSettingChanged;
    }

    public event Action<CategorySyncSnapshot>? SnapshotChanged;

    public CategorySyncSnapshot Snapshot { get; private set; }

    public CategorySyncSnapshot Refresh(string modDirectory)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Publish(new CategorySyncSnapshot(CategorySyncStatus.Disposed, "Base/Default", new HashSet<string>(),
                    "El gestor se ha descargado."));
            }

            try
            {
                if (!PenumbraModIdentityService.IsSafeDirectoryName(modDirectory))
                {
                    return Publish(new CategorySyncSnapshot(CategorySyncStatus.ModUnavailable, "Base/Default", new HashSet<string>(),
                        "El identificador del mod gestionado no es válido."));
                }

                _managedDirectory = modDirectory;
                var collection = _transport.GetDefaultCollection();
                if (collection is null || collection.Id == Guid.Empty)
                {
                    return Publish(new CategorySyncSnapshot(CategorySyncStatus.DefaultCollectionUnavailable, "Base/Default", new HashSet<string>(),
                        "Penumbra no expone una colección Base/Default válida."));
                }

                var available = _transport.GetAvailableSettings(modDirectory);
                if (available is null)
                {
                    return Publish(new CategorySyncSnapshot(CategorySyncStatus.ModUnavailable, collection.Name, new HashSet<string>(),
                        "El mod gestionado no está disponible en Penumbra."));
                }

                if (!available.TryGetValue(TranslationCategoryCatalog.GroupName, out var options))
                {
                    return Publish(new CategorySyncSnapshot(CategorySyncStatus.CategoryGroupMissing, collection.Name, new HashSet<string>(),
                        "El grupo de categorías no existe en el mod instalado; regenera la traducción."));
                }

                var expectedOptions = TranslationCategoryCatalog.All.Select(category => category.DisplayName).ToHashSet(StringComparer.Ordinal);
                if (!expectedOptions.SetEquals(options))
                {
                    return Publish(new CategorySyncSnapshot(CategorySyncStatus.CategoryOptionMissing, collection.Name, new HashSet<string>(),
                        "Las opciones del mod instalado no coinciden con el esquema de categorías actual; regenera la traducción."));
                }

                var persistent = _transport.GetCurrentSettings(collection.Id, modDirectory);
                if (persistent.ResultCode != 0)
                {
                    return Publish(new CategorySyncSnapshot(CategorySyncStatus.ModUnavailable, collection.Name, new HashSet<string>(),
                        $"Penumbra no pudo leer los ajustes del mod (código {persistent.ResultCode})."));
                }

                var temporary = _transport.GetTemporarySettings(collection.Id, modDirectory, SessionLeaseKey);
                if (temporary.ResultCode != 0)
                {
                    return Publish(new CategorySyncSnapshot(CategorySyncStatus.Faulted, collection.Name, new HashSet<string>(),
                        $"Penumbra no pudo leer la activación temporal del mod (código {temporary.ResultCode})."));
                }

                var usesSessionLease = temporary.Exists
                    && StringComparer.Ordinal.Equals(temporary.Source, SessionLeaseSource);
                var enabled = temporary.Exists ? temporary.Enabled : persistent.Enabled;
                var inherited = temporary.Exists ? temporary.ForceInherit : persistent.Inherited;
                var currentOptions = temporary.Exists ? temporary.Options : persistent.Options;
                if (usesSessionLease)
                {
                    _desiredModEnabled = enabled;
                }

                var selectedOptions = currentOptions.TryGetValue(TranslationCategoryCatalog.GroupName, out var selected)
                    ? selected
                    : [];
                var domains = TranslationCategoryCatalog.All
                    .Where(category => selectedOptions.Contains(category.DisplayName, StringComparer.Ordinal))
                    .Select(category => category.Domain)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var status = inherited ? CategorySyncStatus.Inherited : CategorySyncStatus.Ready;
                var message = inherited
                    ? $"Categorías heredadas de la colección Base/Default ({collection.Name})."
                    : $"Categorías sincronizadas con Base/Default ({collection.Name}).";
                var snapshot = Publish(new CategorySyncSnapshot(
                    status,
                    collection.Name,
                    domains,
                    message,
                    enabled,
                    _sessionActivationAllowed,
                    usesSessionLease));
                if (_pendingWrite is { } pending && pending.CollectionId == collection.Id && pending.Domains.SetEquals(domains))
                {
                    _pendingWrite = null;
                }

                return snapshot;
            }
            catch (Exception exception)
            {
                _log.Warning("No se pudieron reconciliar las categorías de Penumbra.", exception);
                return Publish(new CategorySyncSnapshot(CategorySyncStatus.Faulted, "Base/Default", new HashSet<string>(),
                    "No se pudieron consultar las categorías en Penumbra."));
            }
        }
    }

    /// <summary>
    /// Removes leases created by older manager builds and records the desired state without
    /// changing the persistent activation that Penumbra needs during early game startup.
    /// </summary>
    public ManagedModSessionPreparation PrepareSession(string modDirectory, bool? configuredDesiredState)
    {
        lock (_gate)
        {
            _handlingWrite = true;
            try
            {
                var context = ReadPersistentContext(modDirectory);
                if (context is null)
                {
                    var unavailable = Refresh(modDirectory);
                    return new ManagedModSessionPreparation(false, configuredDesiredState ?? true, unavailable);
                }

                _desiredModEnabled = configuredDesiredState ?? context.Settings.Enabled;
                _sessionActivationAllowed = false;
                var removeResult = RemoveOwnSessionLease(context.Collection.Id, modDirectory);
                if (removeResult is not 0 and not 1)
                {
                    var rejected = Publish(Rejected(
                        context.Collection.Name,
                        $"No se pudo retirar la activación temporal de una versión anterior (código {removeResult})."));
                    return new ManagedModSessionPreparation(false, _desiredModEnabled, rejected);
                }

                var snapshot = Refresh(modDirectory);
                return new ManagedModSessionPreparation(snapshot.CanEdit, _desiredModEnabled, snapshot);
            }
            finally
            {
                _handlingWrite = false;
            }
        }
    }

    /// <summary>
    /// Persists the verified desired state so Penumbra can apply EXD resources before the game
    /// requests them on the next boot.
    /// </summary>
    public CategorySyncSnapshot GrantSessionActivation(string modDirectory, bool desiredEnabled)
    {
        lock (_gate)
        {
            _handlingWrite = true;
            try
            {
                _desiredModEnabled = desiredEnabled;
                var context = ReadPersistentContext(modDirectory);
                if (context is null)
                {
                    _sessionActivationAllowed = false;
                    return Refresh(modDirectory);
                }

                var removeResult = RemoveOwnSessionLease(context.Collection.Id, modDirectory);
                if (removeResult is not 0 and not 1)
                {
                    _sessionActivationAllowed = false;
                    return Publish(Rejected(
                        context.Collection.Name,
                        $"No se pudo retirar la activación temporal de una versión anterior (código {removeResult})."));
                }

                var stateResult = context.Settings.Enabled == desiredEnabled
                    ? 1
                    : _transport.SetEnabled(context.Collection.Id, modDirectory, desiredEnabled);
                _sessionActivationAllowed = stateResult is 0 or 1;
                return _sessionActivationAllowed
                    ? Refresh(modDirectory)
                    : Publish(Rejected(
                        context.Collection.Name,
                        $"Penumbra rechazó el estado persistente del mod (código {stateResult})."));
            }
            finally
            {
                _handlingWrite = false;
            }
        }
    }

    public CategorySyncSnapshot RevokeSessionActivation(string modDirectory)
    {
        lock (_gate)
        {
            _handlingWrite = true;
            try
            {
                _sessionActivationAllowed = false;
                var collection = _transport.GetDefaultCollection();
                if (collection is null)
                {
                    return Refresh(modDirectory);
                }

                var result = RemoveOwnSessionLease(collection.Id, modDirectory);
                return result is 0 or 1
                    ? Refresh(modDirectory)
                    : Publish(Rejected(collection.Name,
                        $"Penumbra rechazó la retirada de la activación temporal (código {result})."));
            }
            finally
            {
                _handlingWrite = false;
            }
        }
    }

    /// <summary>Changes only the managed mod state in Base/Default, then reads Penumbra back.</summary>
    public CategorySyncSnapshot SetModEnabled(string modDirectory, bool enabled)
    {
        lock (_gate)
        {
            var current = Refresh(modDirectory);
            if (!current.CanEdit || current.ModEnabled is null)
            {
                return current;
            }

            if (enabled && !_sessionActivationAllowed)
            {
                return Publish(current with
                {
                    Status = CategorySyncStatus.WriteRejected,
                    Message = "La traducción no puede activarse hasta superar la regeneración y validación de esta sesión.",
                });
            }

            _desiredModEnabled = enabled;
            _handlingWrite = true;
            try
            {
                var context = ReadPersistentContext(modDirectory);
                if (context is null)
                {
                    return Refresh(modDirectory);
                }

                var removeResult = RemoveOwnSessionLease(context.Collection.Id, modDirectory);
                if (removeResult is not 0 and not 1)
                {
                    return Publish(current with
                    {
                        Status = CategorySyncStatus.WriteRejected,
                        Message = $"No se pudo retirar la activación temporal de una versión anterior (código {removeResult}).",
                    });
                }

                var result = context.Settings.Enabled == enabled
                    ? 1
                    : _transport.SetEnabled(context.Collection.Id, modDirectory, enabled);
                if (result is not 0 and not 1)
                {
                    return Publish(current with
                    {
                        Status = CategorySyncStatus.WriteRejected,
                        Message = $"Penumbra rechazó el cambio de estado (código {result}); se restauró el estado leído.",
                    });
                }

                _sessionActivationAllowed = true;
                return Refresh(modDirectory);
            }
            finally
            {
                _handlingWrite = false;
            }
        }
    }

    /// <summary>Writes all ten options together. Inherited settings need explicit UI confirmation.</summary>
    public CategorySyncSnapshot SetEnabledDomains(string modDirectory, IReadOnlyCollection<string> domains, bool confirmBreakInheritance)
    {
        lock (_gate)
        {
            var current = Refresh(modDirectory);
            if (!current.CanEdit)
            {
                return current;
            }

            if (current.Status == CategorySyncStatus.Inherited && !confirmBreakInheritance)
            {
                return Publish(current with
                {
                    Message = "Estas categorías se heredan; confirma para crear un ajuste propio en Base/Default.",
                });
            }

            var collection = _transport.GetDefaultCollection();
            if (collection is null)
            {
                return Refresh(modDirectory);
            }

            var selected = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
            var unknown = selected.Where(domain => !TranslationCategoryCatalog.TryGet(domain, out _)).ToArray();
            if (unknown.Length > 0)
            {
                return Publish(current with { Status = CategorySyncStatus.CategoryOptionMissing, Message = "Se solicitó una categoría desconocida." });
            }

            var options = TranslationCategoryCatalog.All
                .Where(category => selected.Contains(category.Domain))
                .Select(category => category.DisplayName)
                .ToArray();
            _handlingWrite = true;
            try
            {
                _pendingWrite = new PendingWrite(++_generation, collection.Id, selected);
                var result = _transport.SetSettings(collection.Id, modDirectory, TranslationCategoryCatalog.GroupName, options);
                if (result != 0)
                {
                    _pendingWrite = null;
                    return Publish(current with
                    {
                        Status = CategorySyncStatus.WriteRejected,
                        Message = $"Penumbra rechazó el cambio de categorías (código {result}); se restauró el estado leído.",
                    });
                }

                return Refresh(modDirectory);
            }
            finally
            {
                _handlingWrite = false;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            if (_managedDirectory is { } directory)
            {
                try
                {
                    var collection = _transport.GetDefaultCollection();
                    if (collection is not null)
                    {
                        RemoveOwnSessionLease(collection.Id, directory);
                    }
                }
                catch (Exception exception)
                {
                    _log.Warning("No se pudo limpiar una activación temporal heredada al descargar el gestor.", exception);
                }
            }

            _disposed = true;
            _transport.ModSettingChanged -= OnModSettingChanged;
            _transport.Dispose();
            Publish(new CategorySyncSnapshot(CategorySyncStatus.Disposed, Snapshot.CollectionName, Snapshot.EnabledDomains,
                "El gestor se ha descargado.", Snapshot.ModEnabled));
            return ValueTask.CompletedTask;
        }
    }

    private void OnModSettingChanged(Guid collectionId, string modDirectory, bool _)
    {
        if (_disposed
            || _handlingWrite
            || !PenumbraModIdentityService.IsSafeDirectoryName(modDirectory)
            || !StringComparer.Ordinal.Equals(_managedDirectory, modDirectory))
        {
            return;
        }

        var collection = _transport.GetDefaultCollection();
        if (collection?.Id != collectionId)
        {
            return;
        }

        // A local write carries both generation and requested snapshot. The event is still read
        // back from Penumbra, but it does not trigger another write or a feedback loop.
        Refresh(modDirectory);
    }

    private PersistentContext? ReadPersistentContext(string modDirectory)
    {
        if (!PenumbraModIdentityService.IsSafeDirectoryName(modDirectory))
        {
            return null;
        }

        _managedDirectory = modDirectory;
        var collection = _transport.GetDefaultCollection();
        if (collection is null || collection.Id == Guid.Empty)
        {
            return null;
        }

        var settings = _transport.GetCurrentSettings(collection.Id, modDirectory);
        return settings.ResultCode == 0 ? new PersistentContext(collection, settings) : null;
    }

    private int RemoveOwnSessionLease(Guid collectionId, string modDirectory)
    {
        var temporary = _transport.GetTemporarySettings(collectionId, modDirectory, SessionLeaseKey);
        if (temporary.ResultCode != 0)
        {
            return temporary.ResultCode;
        }

        if (!temporary.Exists)
        {
            return 1;
        }

        return StringComparer.Ordinal.Equals(temporary.Source, SessionLeaseSource)
            ? _transport.RemoveTemporarySettings(collectionId, modDirectory, SessionLeaseKey)
            : 19;
    }

    private static CategorySyncSnapshot Rejected(string collectionName, string message)
        => new(
            CategorySyncStatus.WriteRejected,
            collectionName,
            new HashSet<string>(),
            message,
            false,
            false,
            false);

    private CategorySyncSnapshot Publish(CategorySyncSnapshot snapshot)
    {
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(snapshot);
        return snapshot;
    }

    private sealed record PendingWrite(long Generation, Guid CollectionId, HashSet<string> Domains);

    private sealed record PersistentContext(PenumbraCollection Collection, PenumbraModSettingsRead Settings);
}
