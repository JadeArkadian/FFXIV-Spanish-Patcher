using System.Diagnostics;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVSpanishPatcher.Pipeline;
using Markdig.Syntax;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Everything the manager does that is not drawing: it loads the configuration, follows Penumbra's
/// availability, keeps the managed mod reconciled with Base/Default, decides when to regenerate and
/// runs the regeneration. The ImGui window reads its state and calls its actions; it holds no UI.
/// </summary>
internal sealed class TranslationManagerSession : IAsyncDisposable
{
    private readonly DalamudServices _dalamud;
    private readonly TranslationManagerNotifications _notifications;
    private readonly PluginLoadProbe _loadProbe;
    private readonly PenumbraIpcClient _penumbra;
    private readonly CategorySyncService _categories;
    private readonly CachedModIdentityResolver _identity;
    private readonly IGameDataBackend _buildInputs;

    public TranslationManagerSession(DalamudServices dalamud, TranslationManagerNotifications notifications)
    {
        _dalamud = dalamud;
        _notifications = notifications;
        _loadProbe = new PluginLoadProbe(new DalamudGameDataProbe(dalamud.DataManager));
        _penumbra = new PenumbraIpcClient(
            new DalamudPenumbraApiTransport(dalamud.PluginInterface),
            PhysicalPenumbraDirectoryProbe.Instance,
            new DalamudPluginDiagnosticLogger(dalamud.Log));
        _categories = new CategorySyncService(
            new DalamudPenumbraCategoryTransport(dalamud.PluginInterface),
            new DalamudPluginDiagnosticLogger(dalamud.Log));
        _identity = new CachedModIdentityResolver(_penumbra);
        _buildInputs = new DalamudBuildInputProvider(typeof(Plugin).Assembly, dalamud.DataManager);
    }

    private IDalamudPluginInterface PluginInterface => _dalamud.PluginInterface;

    private IDataManager DataManager => _dalamud.DataManager;

    private IPluginLog Log => _dalamud.Log;

    private IFramework Framework => _dalamud.Framework;

    // ---- State the window shows. Read-only: every change goes through an action below. ----

    public PluginStateSnapshot State => _controller.Snapshot;

    public DalamudStartupWaitSnapshot StartupWait => _startupWait;

    public PenumbraAvailability PenumbraAvailability => _penumbraAvailability;

    public bool IsPenumbraInstalled => _penumbraInstalled;

    public bool IsPenumbraLoaded => _penumbraLoaded;

    public bool IsPenumbraOutdated => _penumbraOutdated;

    public CategorySyncSnapshot Categories => _categories.Snapshot;

    public PluginConfiguration? Configuration => _configuration;

    public bool IsConfigurationReadOnly => _configurationReadOnly;

    public BuildFingerprint? CurrentFingerprint => _currentFingerprint;

    public string? ManagedModDirectory => _managedModDirectory;

    public bool IsModStateUpdateRunning => _modStateUpdateTask is { IsCompleted: false };

    public string? GenerationProgress => _generationProgress;

    public MarkdownDocument? Milestones => _milestones;

    public ManagedModHeaderSnapshot HeaderSnapshot()
        => ManagedModHeaderStatusModel.Create(
            _controller.Snapshot,
            _startupWait,
            _penumbraAvailability,
            _categories.Snapshot,
            _regeneration,
            _configuration,
            _currentFingerprint,
            _managedInstallationMissing,
            _restartTracker.IsRestartRequired);

    // ---- Actions the window forwards. ----

    public IReadOnlyList<PluginEvent> DrainEvents() => _controller.DrainEvents();

    public void CancelActiveOperation() => _controller.CancelActiveOperation();

    /// <summary>Probes Penumbra on the next framework tick with a fresh retry budget.</summary>
    public void RetryPenumbraNow()
    {
        _penumbraRetryAttempt = 0;
        _nextPenumbraRetryUtc = DateTime.UtcNow;
        _penumbraRetryPending = true;
    }

    public void SetAutomaticRegeneration(bool enabled)
    {
        if (_configuration is not { } configuration || _configurationStore is not { } store)
        {
            return;
        }

        configuration.AutoRegenerationEnabled = enabled;
        store.Save(configuration);
        // Ticking the box is an explicit request, so it lifts a pause left by a cancel.
        _automaticRegenerationPaused = false;
        RefreshRegenerationDecision();
        TryStartAutomaticRegeneration();
    }

    /// <summary>Turns on Dalamud's "wait for plugins" setting; false when Dalamud refused it.</summary>
    public bool TryEnableStartupWait()
    {
        if (!_startupWaitService.TryEnable())
        {
            return false;
        }

        RefreshStartupWait(initialCheck: false);
        return true;
    }

    private const int PenumbraRetryLimit = 8;

    private const int PenumbraInitialRetryMilliseconds = 250;

    private const int PenumbraMaximumRetryMilliseconds = 5000;

    internal const string PenumbraInternalName = "Penumbra";

    private readonly TranslationManagerController _controller = new(SystemPluginClock.Instance);

    private const string StartupWaitGuaranteedKey = "startupWait.guaranteed";

    private static readonly IGameSessionMemory SessionMemory = new ProcessGameSessionMemory();

    private readonly TranslationRestartTracker _restartTracker = new(SessionMemory);

    private readonly DalamudStartupWaitService _startupWaitService = new(typeof(IDalamudPluginInterface).Assembly);

    private PluginConfiguration? _configuration;

    private IPluginConfigurationStore? _configurationStore;

    private bool _configurationReadOnly;

    private PenumbraAvailability _penumbraAvailability = new(
        PenumbraAvailabilityStatus.Unavailable, 0, 0, null,
        "Penumbra aún no se ha comprobado.");

    private bool _penumbraInstalled;

    private bool _penumbraLoaded;

    private bool _penumbraOutdated;

    private int _penumbraPluginListRefreshPending;

    private DalamudStartupWaitSnapshot _startupWait = new(
        DalamudStartupWaitStatus.Unavailable,
        "La carga temprana de Dalamud aún no se ha comprobado.");

    private RegenerationDecision? _regeneration;

    private GameDataBuildInput? _buildInput;

    private BuildFingerprint? _currentFingerprint;

    private string? _managedModDirectory;

    private bool _managedInstallationMissing;

    private bool _initialIdentityResolved;

    private bool _loadCompleted;

    private MarkdownDocument? _milestones;

    private bool _startupWaitGuaranteedForSession;

    private DateTime _nextStartupWaitCheckUtc;

    private DateTime _nextPenumbraRetryUtc;

    private int _penumbraRetryAttempt;

    private bool _penumbraRetryPending;

    private string? _preparedSessionDirectory;

    private Task? _regenerationTask;
    private Task? _identityScan;
    private (CategorySyncStatus Status, string Message)? _lastCategoryLog;
    // Dalamud warns at 50 ms; logging from 15 ms shows where the time goes before it gets there.
    private const double ReconciliationLogThresholdMilliseconds = 15;

    // Set when a regeneration is cancelled or fails. Automatic triggers then stay quiet until the
    // user asks again, so a cancelled update is not restarted behind their back and a failing one
    // does not rebuild on every Penumbra event.
    private bool _automaticRegenerationPaused;

    private volatile string? _generationProgress;

    private Task? _categoryUpdateTask;

    private Task? _modStateUpdateTask;

    /// <summary>Checks that Dalamud-owned game data is readable without taking ownership of it.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        _configurationStore = new DalamudPluginConfigurationStore(PluginInterface);
        _configuration = _configurationStore.Load();
        _configurationReadOnly = _configurationStore.IsReadOnly;
        if (_configurationReadOnly)
        {
            Log.Warning(
                "La configuración usa la versión futura {ConfigurationVersion}; se usarán los ajustes conocidos sin guardar cambios.",
                _configuration.Version);
        }

        RefreshStartupWait(initialCheck: true);
        PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
        RefreshPenumbraPluginPresence();
        Framework.Update += OnFrameworkUpdate;
        await _loadProbe.LoadAsync(cancellationToken).ConfigureAwait(false);
        _penumbra.AvailabilityChanged += OnPenumbraAvailabilityChanged;
        _penumbra.ModDeleted += OnPenumbraModDeleted;
        _penumbra.ModMoved += OnPenumbraModMoved;
        _categories.SnapshotChanged += OnCategorySnapshotChanged;
        _categories.ChangedInPenumbra += OnModChangedInPenumbra;
        // Penumbra IPC belongs on the framework thread. Only a guaranteed Boot load may call it
        // from this worker: Dalamud is then holding that thread until this method returns, so
        // queueing would deadlock startup. Any other load never waits on the framework (the game
        // may still be loading); it defers reconciliation to the next framework tick instead.
        // A reload inherits the session guarantee, but its framework thread is running freely.
        var ownsFrameworkThread = _startupWaitGuaranteedForSession
            && PluginInterface.Reason == PluginLoadReason.Boot;
        if (ownsFrameworkThread)
        {
            var penumbra = await _penumbra.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            OnPenumbraAvailabilityChanged(penumbra);
            _controller.TryTransition(
                penumbra.IsReady ? PluginPhase.WelcomeRequired : PluginPhase.PenumbraUnavailable,
                penumbra.UserMessage,
                penumbra.Status == PenumbraAvailabilityStatus.Faulted ? penumbra.TechnicalError : null);
        }
        else
        {
            _controller.TryTransition(PluginPhase.PenumbraUnavailable, "Comprobando Penumbra…");
        }

        var buildInput = await _buildInputs.GetBuildInputAsync(cancellationToken).ConfigureAwait(false);
        var fingerprint = BuildFingerprintFactory.Create(
            typeof(Plugin).Assembly,
            buildInput,
            BuildFingerprintFactory.ReadRecommendedGameVersion(typeof(Plugin).Assembly),
            DateTimeOffset.UtcNow);
        _buildInput = buildInput;
        _currentFingerprint = fingerprint;
        var milestoneMarkdown = ReadEmbeddedText(
            "FFXIVSpanishTranslationManager.translation-milestones.md",
            "# Hitos de traducción\n\nNo se han podido cargar los hitos incluidos.");
        try
        {
            _milestones = new SafeMarkdownParser().Parse(milestoneMarkdown);
        }
        catch (InvalidDataException exception)
        {
            Log.Warning(exception, "El historial Markdown incluido no es compatible.");
            _milestones = new SafeMarkdownParser().Parse(
                "# Hitos de traducción\n\nNo se ha podido mostrar el historial incluido en esta compilación.");
        }
        _regeneration = RegenerationPlanner.Decide(
            fingerprint,
            _configuration.LastSuccessfulFingerprint,
            _configuration.AutoRegenerationEnabled,
            _managedInstallationMissing);
        Log.Information("Regeneración: {Decision}. {Message}", _regeneration.Kind, _regeneration.Message);
        if (ownsFrameworkThread
            && AutomaticRegenerationPolicy.ShouldCompleteBeforeGameLoad(
                _regeneration,
                _startupWait,
                _penumbraAvailability))
        {
            Log.Information("La regeneración automática terminará durante la carga temprana.");
            _regenerationTask = RegenerateAsync(
                marshalPenumbraToFrameworkThread: false,
                cancellationToken);
            await _regenerationTask.ConfigureAwait(false);
        }

        if (ownsFrameworkThread)
        {
            TryStartAutomaticRegeneration();
        }
        else
        {
            // The tick probes Penumbra, reconciles the mod and starts any pending regeneration.
            SchedulePenumbraRetry(restartRetryBudget: true);
        }

        _loadCompleted = true;
        RecoverControllerAfterPenumbraWait();
        Log.Information(
            "Configuración v{ConfigurationVersion} cargada; regeneración automática: {AutoRegenerationEnabled}.",
            _configuration.Version,
            _configuration.AutoRegenerationEnabled);
        Log.Information("Compatibilidad con Dalamud y motor compartido verificada.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
        Framework.Update -= OnFrameworkUpdate;
        _penumbra.AvailabilityChanged -= OnPenumbraAvailabilityChanged;
        _penumbra.ModDeleted -= OnPenumbraModDeleted;
        _penumbra.ModMoved -= OnPenumbraModMoved;
        _categories.SnapshotChanged -= OnCategorySnapshotChanged;
        _categories.ChangedInPenumbra -= OnModChangedInPenumbra;
        _notifications.DismissTranslationRestart();
        // Stop an in-flight repair before disposing the IPC and category dependencies it uses.
        await _controller.DisposeAsync().ConfigureAwait(false);
        await _categories.DisposeAsync().ConfigureAwait(false);
        await _penumbra.DisposeAsync().ConfigureAwait(false);
        await _loadProbe.DisposeAsync().ConfigureAwait(false);
    }

    private void OnPenumbraAvailabilityChanged(PenumbraAvailability availability)
        => ApplyPenumbraAvailability(availability, restartRetryBudget: true);

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs changes)
    {
        if (changes.AffectedInternalNames.Contains(PenumbraInternalName, StringComparer.Ordinal))
        {
            Interlocked.Exchange(ref _penumbraPluginListRefreshPending, 1);
        }
    }

    private bool RefreshPenumbraPluginPresence()
    {
        // Dev and repository installs can coexist under the same internal name; any loaded one counts.
        var plugins = PluginInterface.InstalledPlugins
            .Where(candidate => StringComparer.Ordinal.Equals(candidate.InternalName, PenumbraInternalName))
            .ToArray();
        var installed = plugins.Length > 0;
        var loaded = plugins.Any(candidate => candidate.IsLoaded);
        var outdated = !loaded && plugins.Any(candidate => candidate.IsOutdated);
        var changed = installed != _penumbraInstalled
            || loaded != _penumbraLoaded
            || outdated != _penumbraOutdated;

        _penumbraInstalled = installed;
        _penumbraLoaded = loaded;
        _penumbraOutdated = outdated;

        if (changed)
        {
            Log.Information(
                "Penumbra instalado: {Installed}; cargado: {Loaded}; desactualizado: {Outdated}.",
                installed,
                loaded,
                outdated);
        }

        return changed;
    }

    public void RecheckPenumbra()
    {
        RefreshPenumbraPluginPresence();
        if (_penumbraLoaded)
        {
            SchedulePenumbraRetry(restartRetryBudget: true);
        }
    }

    private void ApplyPenumbraAvailability(PenumbraAvailability availability, bool restartRetryBudget)
    {
        var availabilityChanged = !SameAvailability(_penumbraAvailability, availability);
        _penumbraAvailability = availability;
        if (availabilityChanged)
        {
            Log.Information("Penumbra: {Status}. {Message}", availability.Status, availability.UserMessage);
        }

        if (!availability.IsReady || String.IsNullOrWhiteSpace(availability.ModDirectory))
        {
            _managedModDirectory = null;
            _managedInstallationMissing = false;
            _preparedSessionDirectory = null;
            SchedulePenumbraRetry(restartRetryBudget);
            return;
        }

        _penumbraRetryPending = false;
        _penumbraRetryAttempt = 0;
        var reconciliationSucceeded = false;
        try
        {
            var modRoot = availability.ModDirectory;
            if (!_identity.TryGetCached(modRoot, out var identity, out var modDirectories))
            {
                if (Framework.IsInFrameworkUpdateThread)
                {
                    // Reading every mod's metadata would stall this frame. Do it on a worker and
                    // let its result resume the reconciliation on a later tick.
                    StartIdentityScan(modRoot, modDirectories);
                    return;
                }

                // A guaranteed Boot load reconciles on its own worker: nothing to stall.
                identity = _identity.Scan(modRoot, modDirectories);
            }

            if (!_initialIdentityResolved)
            {
                _initialIdentityResolved = true;
            }
            _managedInstallationMissing = identity.Kind == ModIdentityResolutionKind.NewInstallation;
            if (identity.Selected is not { } selected)
            {
                _managedModDirectory = null;
                _preparedSessionDirectory = null;
                RefreshRegenerationDecision();
                RecoverControllerAfterPenumbraWait();
                TryStartAutomaticRegeneration();
                return;
            }

            _managedModDirectory = selected.DirectoryName;
            var preparationSucceeded = true;
            if (!StringComparer.Ordinal.Equals(_preparedSessionDirectory, selected.DirectoryName))
            {
                var preparation = _categories.PrepareSession(
                    selected.DirectoryName,
                    _configuration?.ManagedModEnabled);
                if (_configuration is { } configuration
                    && _configurationStore is { } store
                    && configuration.ManagedModEnabled != preparation.DesiredEnabled)
                {
                    configuration.ManagedModEnabled = preparation.DesiredEnabled;
                    store.Save(configuration);
                }

                preparationSucceeded = preparation.Succeeded;
                if (preparationSucceeded)
                {
                    _preparedSessionDirectory = selected.DirectoryName;
                }
                else
                {
                    Log.Warning("No se pudo preparar la activación segura del mod gestionado.");
                }
            }

            RefreshRegenerationDecision();
            var activationSucceeded = TryActivateVerifiedSession();
            reconciliationSucceeded = preparationSucceeded && activationSucceeded;
            RecoverControllerAfterPenumbraWait();
            TryStartAutomaticRegeneration();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "No se pudieron comprobar las categorías de Penumbra.");
        }

        if (!reconciliationSucceeded)
        {
            SchedulePenumbraRetry(restartRetryBudget: false);
        }
    }

    private void StartIdentityScan(string modRoot, string[] modDirectories)
    {
        if (_identityScan is { IsCompleted: false })
        {
            return; // Its result already resumes the reconciliation.
        }

        _identityScan = Task.Run(() =>
        {
            try
            {
                var started = Stopwatch.GetTimestamp();
                _identity.Scan(modRoot, modDirectories);
                Log.Information(
                    "Biblioteca de Penumbra leída fuera del hilo del juego: {Count} mods en {Milliseconds:0} ms.",
                    modDirectories.Length,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                // The answer is cached now; the next tick picks it up without reading any file.
                RetryPenumbraNow();
            }
            catch (Exception exception)
            {
                // Not rescheduled: a scan that fails would fail again on every tick.
                Log.Warning(exception, "No se pudo leer la biblioteca de mods de Penumbra.");
            }
        });
    }

    private void OnPenumbraModDeleted(string directoryName)
    {
        if (String.IsNullOrWhiteSpace(_managedModDirectory)
            || !StringComparer.Ordinal.Equals(_managedModDirectory, directoryName))
        {
            return;
        }

        Log.Information("Penumbra ha eliminado el mod gestionado; se programa su reparación automática.");
        _managedModDirectory = null;
        _preparedSessionDirectory = null;
        _managedInstallationMissing = true;
        RefreshRegenerationDecision();
        TryStartAutomaticRegeneration();
    }

    private void OnPenumbraModMoved(string oldDirectoryName, string newDirectoryName)
    {
        if (!StringComparer.Ordinal.Equals(_managedModDirectory, oldDirectoryName)
            || !PenumbraModIdentityService.IsSafeDirectoryName(newDirectoryName))
        {
            return;
        }

        _managedModDirectory = newDirectoryName;
        var preparation = _categories.PrepareSession(newDirectoryName, _configuration?.ManagedModEnabled);
        if (preparation.Succeeded)
        {
            _preparedSessionDirectory = newDirectoryName;
        }
        else
        {
            Log.Warning("No se pudo preparar la activación segura tras renombrar el mod gestionado.");
        }
        TryActivateVerifiedSession();
        Log.Information("Penumbra ha renombrado el directorio del mod gestionado a {DirectoryName}.", newDirectoryName);
    }

    private void OnCategorySnapshotChanged(CategorySyncSnapshot categories)
    {
        // Every click reads Penumbra back twice; the line is worth writing only when it says something new.
        var categoryLog = (categories.Status, categories.Message);
        if (categoryLog != _lastCategoryLog)
        {
            _lastCategoryLog = categoryLog;
            Log.Information("Categorías: {Status}. {Message}", categories.Status, categories.Message);
        }

        ObserveTranslationRuntimeState(categories);
        if (!categories.UsesSessionLease
            || categories.ModEnabled is not { } enabled
            || _configuration is not { } configuration
            || _configurationStore is not { } store
            || configuration.ManagedModEnabled == enabled)
        {
            return;
        }

        configuration.ManagedModEnabled = enabled;
        store.Save(configuration);
    }

    /// <summary>The user changed the mod in Penumbra's window: follow them instead of undoing it.</summary>
    private void OnModChangedInPenumbra(CategorySyncSnapshot categories)
    {
        if (_configuration is not { } configuration
            || !ManagedModActivationPolicy.ShouldAdoptPenumbraState(
                categories,
                configuration.ManagedModEnabled,
                regenerationRunning: _regenerationTask is { IsCompleted: false }))
        {
            return;
        }

        var enabled = categories.ModEnabled!.Value;
        Log.Information(
            "Penumbra ha cambiado el estado del mod; el gestor lo adopta: {State}.",
            enabled ? "traducción activa" : "traducción desactivada");
        PersistModState(enabled);
    }

    /// <summary>Records the agreed state; reaching agreement also settles any owed activation.</summary>
    private void PersistModState(bool enabled)
    {
        if (_configuration is not { } configuration
            || (configuration.ManagedModEnabled == enabled && !configuration.ActivationPending))
        {
            return;
        }

        configuration.ManagedModEnabled = enabled;
        configuration.ActivationPending = false;
        _configurationStore?.Save(configuration);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        if (Interlocked.Exchange(ref _penumbraPluginListRefreshPending, 0) != 0
            && RefreshPenumbraPluginPresence())
        {
            SchedulePenumbraRetry(restartRetryBudget: true);
        }

        if (now >= _nextStartupWaitCheckUtc)
        {
            _nextStartupWaitCheckUtc = now.AddSeconds(1);
            RefreshStartupWait(initialCheck: false);
        }

        if (_penumbraRetryPending && now >= _nextPenumbraRetryUtc)
        {
            _penumbraRetryPending = false;
            _penumbraRetryAttempt++;
            var started = Stopwatch.GetTimestamp();
            var availability = _penumbra.ProbeAvailability();
            var probe = Stopwatch.GetElapsedTime(started);
            ApplyPenumbraAvailability(availability, restartRetryBudget: false);
            var total = Stopwatch.GetElapsedTime(started);
            if (total.TotalMilliseconds >= ReconciliationLogThresholdMilliseconds)
            {
                Log.Information(
                    "Reconciliación con Penumbra: {Total:0} ms en el hilo del juego (sondeo {Probe:0} ms).",
                    total.TotalMilliseconds,
                    probe.TotalMilliseconds);
            }
        }
    }

    private void SchedulePenumbraRetry(bool restartRetryBudget)
    {
        if (restartRetryBudget)
        {
            _penumbraRetryAttempt = 0;
        }

        if (_penumbraRetryAttempt >= PenumbraRetryLimit)
        {
            _penumbraRetryPending = false;
            return;
        }

        var exponent = Math.Min(_penumbraRetryAttempt, 5);
        var delayMilliseconds = Math.Min(
            PenumbraInitialRetryMilliseconds * (1 << exponent),
            PenumbraMaximumRetryMilliseconds);
        _nextPenumbraRetryUtc = DateTime.UtcNow.AddMilliseconds(delayMilliseconds);
        _penumbraRetryPending = true;
    }

    private void RecoverControllerAfterPenumbraWait()
    {
        if (_loadCompleted
            && _penumbraAvailability.IsReady
            && _controller.Snapshot.Phase == PluginPhase.PenumbraUnavailable)
        {
            _controller.TryTransition(
                PluginPhase.Ready,
                "Penumbra está disponible; estado del mod reconciliado.");
        }
    }

    private static bool SameAvailability(PenumbraAvailability left, PenumbraAvailability right)
        => left.Status == right.Status
            && left.BreakingVersion == right.BreakingVersion
            && left.FeatureVersion == right.FeatureVersion
            && StringComparer.Ordinal.Equals(left.ModDirectory, right.ModDirectory)
            && StringComparer.Ordinal.Equals(left.UserMessage, right.UserMessage);

    private void RefreshStartupWait(bool initialCheck)
    {
        var enabled = _startupWaitService.TryRead(out var currentValue)
            ? currentValue
            : (bool?)null;
        if (initialCheck)
        {
            _startupWaitGuaranteedForSession = DalamudStartupWaitPolicy.IsGuaranteedForCurrentSession(
                enabled,
                PluginInterface.Reason,
                SessionMemory.Get(StartupWaitGuaranteedKey) is not null);
            if (_startupWaitGuaranteedForSession)
            {
                SessionMemory.Set(StartupWaitGuaranteedKey, "1");
            }
        }

        var previous = _startupWait;
        var current = DalamudStartupWaitPolicy.Evaluate(enabled, _startupWaitGuaranteedForSession);
        _startupWait = current;
        if (!initialCheck && current.Status == previous.Status)
        {
            return;
        }

        Log.Information("Carga temprana de Dalamud: {Status}. {Message}", current.Status, current.Message);
        if (!current.IsReady)
        {
            _notifications.ShowStartupWait(current);
        }
        else if (!previous.IsReady)
        {
            TryStartAutomaticRegeneration();
        }
    }

    public TranslationManagerUiSnapshot UiSnapshot()
        => TranslationManagerUiModel.Create(
            _controller.Snapshot,
            _startupWait,
            _penumbraAvailability,
            _categories.Snapshot,
            _regeneration,
            _configuration,
            _currentFingerprint,
            _managedInstallationMissing,
            _restartTracker.IsRestartRequired);

    public void StartCategoryUpdate(IReadOnlyCollection<string> domains, bool confirmBreakInheritance)
    {
        if (!_startupWait.IsReady
            || _categoryUpdateTask is { IsCompleted: false }
            || String.IsNullOrWhiteSpace(_managedModDirectory))
        {
            return;
        }

        var directory = _managedModDirectory;
        _categoryUpdateTask = UpdateCategoriesAsync(directory, domains.ToArray(), confirmBreakInheritance);
    }

    private async Task UpdateCategoriesAsync(string directory, IReadOnlyCollection<string> domains, bool confirmBreakInheritance)
    {
        try
        {
            await Framework.Run(
                    () => _categories.SetEnabledDomains(directory, domains, confirmBreakInheritance))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "No se pudieron actualizar las categorías de traducción.");
        }
    }

    public void StartModStateUpdate(bool enabled)
    {
        if (_managedModDirectory is not { } directory
            || _modStateUpdateTask is { IsCompleted: false }
            || (enabled && !_categories.Snapshot.SessionActivationAllowed))
        {
            return;
        }

        _modStateUpdateTask = UpdateModStateAsync(directory, enabled);
    }

    private async Task UpdateModStateAsync(string directory, bool enabled)
    {
        try
        {
            var snapshot = await Framework.Run(() => _categories.SetModEnabled(directory, enabled)).ConfigureAwait(false);
            var applied = ManagedModActivationPolicy.IsSessionStateApplied(snapshot, enabled);
            if (applied && _configuration is { } configuration && _configurationStore is { } store)
            {
                configuration.ManagedModEnabled = enabled;
                store.Save(configuration);
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "No se pudo sincronizar el estado del mod con Penumbra.");
            // This continuation runs on a worker; the read-back is IPC, so it is queued instead.
            _ = Framework.RunOnFrameworkThread(() => _categories.Refresh(directory));
        }
    }

    private static string ReadEmbeddedText(string resourceName, string fallback)
    {
        try
        {
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return fallback;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return fallback;
        }
    }

    public void StartRegeneration(bool requestedByUser = false)
    {
        if (!_startupWait.IsReady || _regenerationTask is { IsCompleted: false })
        {
            return;
        }

        if (requestedByUser)
        {
            _automaticRegenerationPaused = false;
        }

        _regenerationTask = RegenerateAsync(
            marshalPenumbraToFrameworkThread: true,
            CancellationToken.None);
    }

    private void TryStartAutomaticRegeneration()
    {
        TryActivateVerifiedSession();
        if (_startupWait.IsReady
            && !_automaticRegenerationPaused
            && _regeneration?.RequiresBuild == true
            && _penumbraAvailability.IsReady
            // Several Penumbra events can arrive while one regeneration runs; only the first starts it.
            && _regenerationTask is not { IsCompleted: false })
        {
            Log.Information("Se inicia la regeneración automática de la traducción.");
            StartRegeneration();
        }
    }

    private bool TryActivateVerifiedSession()
    {
        if (_regeneration?.Kind != RegenerationDecisionKind.UpToDate)
        {
            return true;
        }

        if (!_penumbraAvailability.IsReady
            || _managedModDirectory is not { } directory
            || _configuration is not { } configuration)
        {
            return false;
        }

        var current = _categories.Snapshot;
        var desired = ManagedModActivationPolicy.DesiredAtReconciliation(
            configuration.ActivationPending,
            configuration.ManagedModEnabled,
            current.CanEdit ? current.ModEnabled : null);
        if (ManagedModActivationPolicy.IsSessionStateApplied(current, desired))
        {
            PersistModState(desired);
            return true;
        }

        var snapshot = _categories.GrantSessionActivation(directory, desired);
        if (!ManagedModActivationPolicy.IsSessionStateApplied(snapshot, desired))
        {
            Log.Warning("La instalación está actualizada, pero Penumbra rechazó su estado persistente.");
            return false;
        }

        PersistModState(desired);
        Log.Information(
            "Estado persistente: {State}.",
            desired ? "traducción activa antes del próximo arranque" : "traducción desactivada por elección del usuario");
        return true;
    }

    private void RefreshRegenerationDecision()
    {
        if (_currentFingerprint is not { } fingerprint || _configuration is not { } configuration)
        {
            return;
        }

        _regeneration = RegenerationPlanner.Decide(
            fingerprint,
            configuration.LastSuccessfulFingerprint,
            configuration.AutoRegenerationEnabled,
            _managedInstallationMissing);
    }

    private async Task RegenerateAsync(
        bool marshalPenumbraToFrameworkThread,
        CancellationToken cancellationToken)
    {
        var attempted = false;
        var succeeded = false;
        try
        {
            var configuration = _configuration
                ?? throw new InvalidOperationException("La configuración aún no se ha cargado.");
            var configurationStore = _configurationStore
                ?? throw new InvalidOperationException("El almacenamiento de configuración aún no está disponible.");
            var availability = _penumbraAvailability;
            if (!availability.IsReady || String.IsNullOrWhiteSpace(availability.ModDirectory))
            {
                _controller.TryTransition(PluginPhase.PenumbraUnavailable, "Penumbra no está disponible para instalar la traducción.");
                return;
            }

            var buildInput = await _buildInputs.GetBuildInputAsync(cancellationToken).ConfigureAwait(false);
            var fingerprint = BuildFingerprintFactory.Create(
                typeof(Plugin).Assembly,
                buildInput,
                BuildFingerprintFactory.ReadRecommendedGameVersion(typeof(Plugin).Assembly),
                DateTimeOffset.UtcNow);
            _controller.TryTransition(PluginPhase.Checking, "Preparando una generación verificable.");

            var pipeline = new PatchPipeline(
                EmbeddedTranslationSource.FromAssemblyResource(typeof(Plugin).Assembly,
                    "FFXIVSpanishTranslationManager.translations.dat"),
                new DalamudPatchBackendFactory(DataManager));
            var metadata = PackageMetadataFactory.Create(
                PackageMetadataFactory.PackageVersionFromAssembly(typeof(Plugin).Assembly),
                GameVersionText.IsUnknown(buildInput.InstalledGameVersion) ? null : buildInput.InstalledGameVersion,
                DateTimeOffset.Now);
            var treeBuilder = new PipelineTreeBuilder(
                pipeline,
                request => BuildPatchRequest(request, availability.ModDirectory!, metadata),
                new LatestPipelineMessage(message => _generationProgress = message));
            // During a guaranteed Boot load Dalamud is deliberately holding the framework thread
            // until this method returns. Penumbra is already Ready, so its synchronous IPC must run
            // on this load worker; queueing it to Framework would deadlock startup. Every later write
            // keeps the normal framework-thread adapter.
            IPenumbraModRegistry registry = marshalPenumbraToFrameworkThread
                ? new FrameworkPenumbraModRegistry(_penumbra, Framework)
                : _penumbra;
            // The category IPC that follows the installation obeys the same rule as the registry.
            Task<CategorySyncSnapshot> OnPenumbraThread(Func<CategorySyncSnapshot> ipc)
                => marshalPenumbraToFrameworkThread
                    ? Framework.RunOnFrameworkThread(ipc)
                    : Task.FromResult(ipc());
            var installer = new ModInstaller(registry, new DalamudPluginDiagnosticLogger(Log));
            var service = new TranslationRegenerationService(treeBuilder, installer, configurationStore, configuration);
            var enableInDefaultCollection = ManagedModActivationPolicy.DesiredAfterRegeneration(
                configuration.LastSuccessfulFingerprint,
                _categories.Snapshot.ModEnabled,
                configuration.ManagedModEnabled);
            var candidateTree = Path.Combine(
                Path.GetTempPath(),
                "ffxivspanish-translation-manager",
                $"candidate-{Guid.NewGuid():N}");
            var sessionWasValidated = _categories.Snapshot.SessionActivationAllowed;
            attempted = true;
            var execution = await _controller.RunExclusiveAsync(
                PluginPhase.Generating,
                async cancellationToken =>
                {
                    var install = await service.RegenerateAsync(
                        new TreeBuildRequest(candidateTree, fingerprint),
                        availability.ModDirectory!,
                        enableInDefaultCollection: false,
                        cancellationToken)
                        .ConfigureAwait(false);
                    if (!install.Succeeded || install.ModDirectory is not { } installedDirectory)
                    {
                        if (sessionWasValidated && _managedModDirectory is { } previousDirectory)
                        {
                            var restored = await OnPenumbraThread(
                                    () => _categories.GrantSessionActivation(previousDirectory, enableInDefaultCollection))
                                .ConfigureAwait(false);
                            if (ManagedModActivationPolicy.IsSessionStateApplied(restored, enableInDefaultCollection))
                            {
                                PersistModState(enableInDefaultCollection);
                            }
                        }

                        return (new RegenerationExecution(install, false),
                            PluginPhase.ErrorRecoverable,
                            install.Message);
                    }

                    var activation = await OnPenumbraThread(
                            () => _categories.GrantSessionActivation(installedDirectory, enableInDefaultCollection))
                        .ConfigureAwait(false);
                    var activationApplied = ManagedModActivationPolicy.IsSessionStateApplied(
                        activation,
                        enableInDefaultCollection);
                    if (activationApplied)
                    {
                        PersistModState(enableInDefaultCollection);
                    }

                    var message = activationApplied
                        ? install.Message
                        : install.Message + " El mod queda bloqueado porque Penumbra rechazó su estado persistente.";
                    return (new RegenerationExecution(install, activationApplied),
                        activationApplied ? PluginPhase.Ready : PluginPhase.ErrorRecoverable,
                        message);
                }, cancellationToken).ConfigureAwait(false);

            var result = execution.Install;
            succeeded = result.Succeeded;

            if (result.Succeeded && result.ModDirectory is { } directory)
            {
                _managedInstallationMissing = false;
                _managedModDirectory = directory;
                if (execution.ActivationApplied)
                {
                    _preparedSessionDirectory = directory;
                }
                await OnPenumbraThread(() => _categories.Refresh(directory)).ConfigureAwait(false);
                MarkTranslationFilesRebuilt();
            }
            _regeneration = RegenerationPlanner.Decide(
                fingerprint,
                configuration.LastSuccessfulFingerprint,
                configuration.AutoRegenerationEnabled,
                _managedInstallationMissing);
            Log.Information(
                "Regeneración: {Succeeded}; activación segura: {ActivationApplied}. {Message}",
                result.Succeeded,
                execution.ActivationApplied,
                result.Message);
        }
        catch (OperationCanceledException)
        {
            Log.Information("La regeneración fue cancelada.");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "La regeneración de la traducción falló.");
        }
        finally
        {
            _generationProgress = null;
            if (attempted)
            {
                // The installation rewrites the tree without necessarily changing the mod list.
                _identity.Invalidate();
            }

            if (attempted && !succeeded)
            {
                _automaticRegenerationPaused = true;
                // A cancel or failure can land mid-install; read the real state back from Penumbra
                // and the disk instead of assuming what is installed.
                SchedulePenumbraRetry(restartRetryBudget: true);
            }
        }
    }

    /// <summary>Publishes the newest user-facing pipeline step for the status card.</summary>
    private sealed class LatestPipelineMessage(Action<string> publish) : IProgress<PipelineEvent>
    {
        public void Report(PipelineEvent value)
        {
            if (value.Level != PipelineLevel.Debug)
            {
                publish(value.Message);
            }
        }
    }

    private void ObserveTranslationRuntimeState(CategorySyncSnapshot snapshot)
    {
        var wasRequired = _restartTracker.IsRestartRequired;
        _restartTracker.Observe(snapshot);
        UpdateTranslationRestartNotification(wasRequired);
    }

    private void MarkTranslationFilesRebuilt()
    {
        var wasRequired = _restartTracker.IsRestartRequired;
        _restartTracker.MarkFilesRebuilt();
        UpdateTranslationRestartNotification(wasRequired);
    }

    private void UpdateTranslationRestartNotification(bool wasRequired)
    {
        var isRequired = _restartTracker.IsRestartRequired;
        if (isRequired == wasRequired)
        {
            return;
        }

        if (isRequired)
        {
            _notifications.ShowTranslationRestart();
        }
        else
        {
            _notifications.DismissTranslationRestart();
        }
    }

    private static PatchRequest BuildPatchRequest(TreeBuildRequest request, string modDirectory, PackageMeta metadata)
        => new()
        {
            TreeOutputPath = request.CandidateTreePath,
            OutputPath = Path.Combine(modDirectory, ".ffxivspanish-transient-output.pmp"),
            StagingPath = Path.Combine(Path.GetTempPath(), "ffxivspanish-translation-manager", "pipeline-staging"),
            CompatibilityMode = PatchCompatibilityMode.BestEffortVersionMismatch,
            Meta = metadata,
        };

    private sealed record RegenerationExecution(ModInstallResult Install, bool ActivationApplied);
}
