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
    string Message)
{
    public bool CanEdit => Status is CategorySyncStatus.Ready or CategorySyncStatus.Inherited;
}

/// <summary>
/// Maintains exactly the stable category group in Penumbra's Base/Default collection. It never
/// reads or writes Current, Interface or individual collections, and it reconciles after every
/// write instead of treating the local request as truth.
/// </summary>
public sealed class CategorySyncService : IAsyncDisposable
{
    private readonly IPenumbraCategoryTransport _transport;
    private readonly IPluginDiagnosticLogger _log;
    private readonly object _gate = new();
    private long _generation;
    private PendingWrite? _pendingWrite;
    private string? _managedDirectory;
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

                var settings = _transport.GetCurrentSettings(collection.Id, modDirectory);
                if (settings.ResultCode != 0)
                {
                    return Publish(new CategorySyncSnapshot(CategorySyncStatus.ModUnavailable, collection.Name, new HashSet<string>(),
                        $"Penumbra no pudo leer los ajustes del mod (código {settings.ResultCode})."));
                }

                var selectedOptions = settings.Options.TryGetValue(TranslationCategoryCatalog.GroupName, out var selected)
                    ? selected
                    : [];
                var domains = TranslationCategoryCatalog.All
                    .Where(category => selectedOptions.Contains(category.DisplayName, StringComparer.Ordinal))
                    .Select(category => category.Domain)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var status = settings.Inherited ? CategorySyncStatus.Inherited : CategorySyncStatus.Ready;
                var message = settings.Inherited
                    ? $"Categorías heredadas de la colección Base/Default ({collection.Name})."
                    : $"Categorías sincronizadas con Base/Default ({collection.Name}).";
                var snapshot = Publish(new CategorySyncSnapshot(status, collection.Name, domains, message));
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
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _transport.ModSettingChanged -= OnModSettingChanged;
            _transport.Dispose();
            Publish(new CategorySyncSnapshot(CategorySyncStatus.Disposed, Snapshot.CollectionName, Snapshot.EnabledDomains,
                "El gestor se ha descargado."));
            return ValueTask.CompletedTask;
        }
    }

    private void OnModSettingChanged(Guid collectionId, string modDirectory, bool _)
    {
        if (_disposed
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

    private CategorySyncSnapshot Publish(CategorySyncSnapshot snapshot)
    {
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(snapshot);
        return snapshot;
    }

    private sealed record PendingWrite(long Generation, Guid CollectionId, HashSet<string> Domains);
}
