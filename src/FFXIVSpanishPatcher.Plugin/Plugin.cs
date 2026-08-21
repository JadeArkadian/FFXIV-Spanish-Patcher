using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Minimal Dalamud entrypoint used to prove shared-engine compatibility.</summary>
public sealed class Plugin : IAsyncDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly PluginLoadProbe _loadProbe = new(new DalamudGameDataProbe(DataManager));
    private readonly TranslationManagerController _controller = new(SystemPluginClock.Instance);
    private readonly PenumbraIpcClient _penumbra = new(
        new DalamudPenumbraApiTransport(PluginInterface),
        PhysicalPenumbraDirectoryProbe.Instance,
        new DalamudPluginDiagnosticLogger(Log));
    private readonly CategorySyncService _categories = new(
        new DalamudPenumbraCategoryTransport(PluginInterface),
        new DalamudPluginDiagnosticLogger(Log));
    private PluginConfiguration? _configuration;

    /// <summary>Checks that Dalamud-owned game data is readable without taking ownership of it.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var configurationStore = new DalamudPluginConfigurationStore(PluginInterface);
        _configuration = configurationStore.Load();
        if (_configuration.Version != PluginConfiguration.CurrentVersion)
        {
            throw new InvalidOperationException("La configuración del plugin no pudo migrarse.");
        }

        await _loadProbe.LoadAsync(cancellationToken).ConfigureAwait(false);
        _penumbra.AvailabilityChanged += OnPenumbraAvailabilityChanged;
        _categories.SnapshotChanged += OnCategorySnapshotChanged;
        var penumbra = await _penumbra.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        OnPenumbraAvailabilityChanged(penumbra);
        _controller.TryTransition(
            penumbra.IsReady ? PluginPhase.WelcomeRequired : PluginPhase.PenumbraUnavailable,
            penumbra.UserMessage,
            penumbra.TechnicalError);
        Log.Information(
            "Configuración v{ConfigurationVersion} cargada; regeneración automática: {AutoRegenerationEnabled}.",
            _configuration.Version,
            _configuration.AutoRegenerationEnabled);
        Log.Information("Compatibilidad con Dalamud y motor compartido verificada.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _penumbra.AvailabilityChanged -= OnPenumbraAvailabilityChanged;
        _categories.SnapshotChanged -= OnCategorySnapshotChanged;
        await _categories.DisposeAsync().ConfigureAwait(false);
        await _penumbra.DisposeAsync().ConfigureAwait(false);
        await _controller.DisposeAsync().ConfigureAwait(false);
        await _loadProbe.DisposeAsync().ConfigureAwait(false);
    }

    private void OnPenumbraAvailabilityChanged(PenumbraAvailability availability)
    {
        Log.Information("Penumbra: {Status}. {Message}", availability.Status, availability.UserMessage);
        if (!availability.IsReady || String.IsNullOrWhiteSpace(availability.ModDirectory))
        {
            return;
        }

        try
        {
            var identity = new PenumbraModIdentityService(_penumbra).Resolve(availability.ModDirectory);
            if (identity.Selected is not { } selected)
            {
                return;
            }

            _categories.Refresh(selected.DirectoryName);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "No se pudieron comprobar las categorías de Penumbra.");
        }
    }

    private void OnCategorySnapshotChanged(CategorySyncSnapshot categories)
        => Log.Information("Categorías: {Status}. {Message}", categories.Status, categories.Message);
}
