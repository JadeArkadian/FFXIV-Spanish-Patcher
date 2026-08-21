using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using Dalamud.Bindings.ImGui;
using FFXIVSpanishPatcher.Pipeline;
using System.Numerics;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Minimal Dalamud entrypoint used to prove shared-engine compatibility.</summary>
public sealed class Plugin : IAsyncDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private readonly PluginLoadProbe _loadProbe = new(new DalamudGameDataProbe(DataManager));
    private readonly TranslationManagerController _controller = new(SystemPluginClock.Instance);
    private readonly PenumbraIpcClient _penumbra = new(
        new DalamudPenumbraApiTransport(PluginInterface),
        PhysicalPenumbraDirectoryProbe.Instance,
        new DalamudPluginDiagnosticLogger(Log));
    private readonly CategorySyncService _categories = new(
        new DalamudPenumbraCategoryTransport(PluginInterface),
        new DalamudPluginDiagnosticLogger(Log));
    private readonly IGameDataBackend _buildInputs = new DalamudBuildInputProvider(typeof(Plugin).Assembly, DataManager);
    private PluginConfiguration? _configuration;
    private IPluginConfigurationStore? _configurationStore;
    private PenumbraAvailability _penumbraAvailability = new(
        PenumbraAvailabilityStatus.Unavailable, 0, 0, null,
        "Penumbra aún no se ha comprobado.");
    private RegenerationDecision? _regeneration;
    private GameDataBuildInput? _buildInput;
    private BuildFingerprint? _currentFingerprint;
    private string? _managedModDirectory;
    private readonly List<PluginEvent> _eventHistory = [];
    private string? _pendingInheritedCategoryDomain;
    private TranslationManagerTab _activeTab;
    private string _milestones = "No se han podido cargar los hitos incluidos.";
    private string _legalAndCredits = "No se han podido cargar los créditos incluidos.";
    private readonly CommandInfo _openCommand;
    private bool _windowOpen;
    private Task? _regenerationTask;
    private Task? _categoryUpdateTask;

    public Plugin()
    {
        _openCommand = new CommandInfo((_, _) => _windowOpen = true)
        {
            HelpMessage = "Abre FFXIV Spanish Translation Manager.",
        };
        CommandManager.AddHandler("/ffxives", _openCommand);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenWindow;
        PluginInterface.UiBuilder.OpenMainUi += OpenWindow;
    }

    /// <summary>Checks that Dalamud-owned game data is readable without taking ownership of it.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        _configurationStore = new DalamudPluginConfigurationStore(PluginInterface);
        _configuration = _configurationStore.Load();
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
        var buildInput = await _buildInputs.GetBuildInputAsync(cancellationToken).ConfigureAwait(false);
        var fingerprint = BuildFingerprintFactory.Create(
            typeof(Plugin).Assembly,
            buildInput,
            BuildFingerprintFactory.ReadRecommendedGameVersion(typeof(Plugin).Assembly),
            DateTimeOffset.UtcNow);
        _buildInput = buildInput;
        _currentFingerprint = fingerprint;
        _milestones = ReadEmbeddedText("FFXIVSpanishTranslationManager.translation-milestones.md", _milestones);
        _legalAndCredits = ReadEmbeddedText("FFXIVSpanishTranslationManager.legal-and-credits.md", _legalAndCredits);
        _regeneration = RegenerationPlanner.Decide(
            fingerprint,
            _configuration.LastSuccessfulFingerprint,
            _configuration.AutoRegenerationEnabled);
        Log.Information("Regeneración: {Decision}. {Message}", _regeneration.Kind, _regeneration.Message);
        TryStartAutomaticRegeneration();
        Log.Information(
            "Configuración v{ConfigurationVersion} cargada; regeneración automática: {AutoRegenerationEnabled}.",
            _configuration.Version,
            _configuration.AutoRegenerationEnabled);
        Log.Information("Compatibilidad con Dalamud y motor compartido verificada.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenWindow;
        PluginInterface.UiBuilder.OpenMainUi -= OpenWindow;
        CommandManager.RemoveHandler("/ffxives");
        _penumbra.AvailabilityChanged -= OnPenumbraAvailabilityChanged;
        _categories.SnapshotChanged -= OnCategorySnapshotChanged;
        await _categories.DisposeAsync().ConfigureAwait(false);
        await _penumbra.DisposeAsync().ConfigureAwait(false);
        await _controller.DisposeAsync().ConfigureAwait(false);
        await _loadProbe.DisposeAsync().ConfigureAwait(false);
    }

    private void OnPenumbraAvailabilityChanged(PenumbraAvailability availability)
    {
        _penumbraAvailability = availability;
        Log.Information("Penumbra: {Status}. {Message}", availability.Status, availability.UserMessage);
        if (!availability.IsReady || String.IsNullOrWhiteSpace(availability.ModDirectory))
        {
            _managedModDirectory = null;
            return;
        }

        try
        {
            var identity = new PenumbraModIdentityService(_penumbra).Resolve(availability.ModDirectory);
            if (identity.Selected is not { } selected)
            {
                _managedModDirectory = null;
                return;
            }

            _managedModDirectory = selected.DirectoryName;
            _categories.Refresh(selected.DirectoryName);
            TryStartAutomaticRegeneration();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "No se pudieron comprobar las categorías de Penumbra.");
        }
    }

    private void OnCategorySnapshotChanged(CategorySyncSnapshot categories)
        => Log.Information("Categorías: {Status}. {Message}", categories.Status, categories.Message);

    private void OpenWindow() => _windowOpen = true;

    private void Draw()
    {
        if (!_windowOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(720, 560), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520, 400), new Vector2(1100, 1000));
        if (!ImGui.Begin("FFXIV Spanish Translation Manager", ref _windowOpen))
        {
            ImGui.End();
            return;
        }

        DrainEvents();
        if (ImGui.BeginTabBar("Gestor de traducción"))
        {
            DrawTab(TranslationManagerTab.Inicio, "Inicio", DrawHome);
            DrawTab(TranslationManagerTab.Categorias, "Categorías", DrawCategories);
            DrawTab(TranslationManagerTab.Actualizaciones, "Actualizaciones", DrawUpdates);
            DrawTab(TranslationManagerTab.Ajustes, "Ajustes", DrawSettings);
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private void DrawTab(TranslationManagerTab tab, string label, Action draw)
    {
        if (!ImGui.BeginTabItem(label))
        {
            return;
        }

        _activeTab = tab;
        draw();
        ImGui.EndTabItem();
    }

    private void DrawHome()
    {
        var ui = UiSnapshot();
        ImGui.TextUnformatted("Estado de la traducción");
        ImGui.TextWrapped(_controller.Snapshot.Message);
        ImGui.Separator();
        ImGui.TextUnformatted($"Penumbra: {_penumbraAvailability.Status}");
        ImGui.TextWrapped(_penumbraAvailability.UserMessage);
        if (_regeneration is { } regeneration)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(regeneration.Message);
        }

        ImGui.Spacing();
        if (ui.CanStartRegeneration && ImGui.Button(ui.PrimaryActionLabel))
        {
            StartRegeneration();
        }
        else if (ui.CanCancelRegeneration)
        {
            ImGui.TextDisabled("Generando e instalando la traducción…");
            if (ImGui.Button("Cancelar"))
            {
                _controller.CancelActiveOperation();
            }
        }
        else if (!ui.CanStartRegeneration)
        {
            ImGui.TextDisabled("La instalación necesita que Penumbra esté disponible.");
        }
    }

    private void DrawCategories()
    {
        var ui = UiSnapshot();
        var categories = _categories.Snapshot;
        ImGui.TextUnformatted($"Colección administrada: Base/Default ({categories.CollectionName})");
        ImGui.TextWrapped(categories.Message);
        ImGui.Separator();
        if (!ui.CanEditCategories || String.IsNullOrWhiteSpace(_managedModDirectory))
        {
            ImGui.TextDisabled("Las categorías estarán disponibles cuando Penumbra detecte el mod gestionado.");
            return;
        }

        foreach (var category in TranslationCategoryCatalog.All)
        {
            var enabled = categories.EnabledDomains.Contains(category.Domain);
            if (!ImGui.Checkbox(category.DisplayName, ref enabled))
            {
                continue;
            }

            var desired = new HashSet<string>(categories.EnabledDomains, StringComparer.OrdinalIgnoreCase);
            if (enabled)
            {
                desired.Add(category.Domain);
            }
            else
            {
                desired.Remove(category.Domain);
            }

            if (categories.Status == CategorySyncStatus.Inherited)
            {
                _pendingInheritedCategoryDomain = category.Domain;
            }
            else
            {
                StartCategoryUpdate(desired, confirmBreakInheritance: false);
            }
        }

        if (_pendingInheritedCategoryDomain is { } pending)
        {
            ImGui.Separator();
            ImGui.TextWrapped("Estas categorías se heredan. Crear un ajuste propio en Base/Default dejará de heredar cambios de la colección superior.");
            if (ImGui.Button("Crear ajuste propio"))
            {
                var desired = new HashSet<string>(categories.EnabledDomains, StringComparer.OrdinalIgnoreCase);
                if (!desired.Add(pending))
                {
                    desired.Remove(pending);
                }

                StartCategoryUpdate(desired, confirmBreakInheritance: true);
                _pendingInheritedCategoryDomain = null;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancelar cambio"))
            {
                _pendingInheritedCategoryDomain = null;
            }
        }
    }

    private void DrawUpdates()
    {
        var fingerprint = _currentFingerprint ?? _configuration?.LastSuccessfulFingerprint;
        ImGui.TextUnformatted("Versiones y actualización");
        ImGui.TextUnformatted($"Plugin: {fingerprint?.PluginAssemblyVersion ?? "sin datos"}");
        ImGui.TextUnformatted($"FFXIV instalado: {fingerprint?.InstalledGameVersion ?? "sin datos"}");
        ImGui.TextUnformatted($"FFXIV recomendado: {fingerprint?.RecommendedGameVersion ?? "sin datos"}");
        ImGui.TextWrapped(_regeneration?.Message ?? "La compatibilidad aún no se ha comprobado.");
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Hitos de traducción"))
        {
            ImGui.TextWrapped(_milestones);
        }
    }

    private void DrawSettings()
    {
        if (_configuration is { } configuration && _configurationStore is { } store)
        {
            var autoRegeneration = configuration.AutoRegenerationEnabled;
            if (ImGui.Checkbox("Regenerar automáticamente tras una actualización real", ref autoRegeneration))
            {
                configuration.AutoRegenerationEnabled = autoRegeneration;
                store.Save(configuration);
                _regeneration = _currentFingerprint is null
                    ? _regeneration
                    : RegenerationPlanner.Decide(_currentFingerprint, configuration.LastSuccessfulFingerprint, autoRegeneration);
            }
        }

        ImGui.TextWrapped("Las diferencias entre versión instalada y corpus usan el mismo modo best effort verificado que el patcher clásico. Un fallo de verificación siempre bloquea la instalación.");
        ImGui.TextWrapped("El gestor administra únicamente Base/Default. No modifica colecciones individuales, Current ni Interface.");
        ImGui.TextWrapped("Para que la traducción esté lista antes de abrir el juego, activa Wait for plugins before game loads en Dalamud.");
        ImGui.Separator();
        if (ImGui.Button("Copiar diagnóstico"))
        {
            ImGui.SetClipboardText(UiSnapshot().Diagnostic);
        }

        if (ImGui.CollapsingHeader("Historial de esta sesión"))
        {
            foreach (var entry in _eventHistory.TakeLast(20))
            {
                ImGui.TextWrapped($"{entry.TimestampUtc:HH:mm:ss} [{entry.Severity}] {entry.Message}");
            }
        }

        if (ImGui.CollapsingHeader("Créditos y aviso legal"))
        {
            ImGui.TextWrapped(_legalAndCredits);
        }
    }

    private TranslationManagerUiSnapshot UiSnapshot()
        => TranslationManagerUiModel.Create(
            _controller.Snapshot,
            _penumbraAvailability,
            _categories.Snapshot,
            _regeneration,
            _configuration,
            _currentFingerprint);

    private void DrainEvents()
    {
        _eventHistory.AddRange(_controller.DrainEvents());
        if (_eventHistory.Count > 128)
        {
            _eventHistory.RemoveRange(0, _eventHistory.Count - 128);
        }
    }

    private void StartCategoryUpdate(IReadOnlyCollection<string> domains, bool confirmBreakInheritance)
    {
        if (_categoryUpdateTask is { IsCompleted: false } || String.IsNullOrWhiteSpace(_managedModDirectory))
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

    private void StartRegeneration()
    {
        if (_regenerationTask is { IsCompleted: false })
        {
            return;
        }

        _regenerationTask = RegenerateAsync();
    }

    private void TryStartAutomaticRegeneration()
    {
        if (_regeneration?.RequiresBuild == true && _penumbraAvailability.IsReady)
        {
            Log.Information("Se inicia la regeneración automática de la traducción.");
            StartRegeneration();
        }
    }

    private async Task RegenerateAsync()
    {
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

            var buildInput = await _buildInputs.GetBuildInputAsync(CancellationToken.None).ConfigureAwait(false);
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
                buildInput.InstalledGameVersion == "desconocida" ? null : buildInput.InstalledGameVersion,
                TranslationCategoryCatalog.All.Select(category => category.DisplayName),
                TranslationCategoryCatalog.All.Select(category => category.DisplayName));
            var treeBuilder = new PipelineTreeBuilder(
                pipeline,
                request => BuildPatchRequest(request, availability.ModDirectory!, metadata));
            var installer = new ModInstaller(
                new FrameworkPenumbraModRegistry(_penumbra, Framework),
                new DalamudPluginDiagnosticLogger(Log));
            var service = new TranslationRegenerationService(treeBuilder, installer, configurationStore, configuration);
            var candidateTree = Path.Combine(
                Path.GetTempPath(),
                "ffxivspanish-translation-manager",
                $"candidate-{Guid.NewGuid():N}");
            var result = await _controller.RunExclusiveAsync(
                PluginPhase.Generating,
                async cancellationToken =>
                {
                    var install = await service.RegenerateAsync(
                            new TreeBuildRequest(candidateTree, fingerprint),
                            availability.ModDirectory!,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return (install,
                        install.Succeeded ? PluginPhase.Ready : PluginPhase.ErrorRecoverable,
                        install.Message);
                }).ConfigureAwait(false);

            _regeneration = RegenerationPlanner.Decide(fingerprint, configuration.LastSuccessfulFingerprint,
                configuration.AutoRegenerationEnabled);
            if (result.Succeeded && result.ModDirectory is { } directory)
            {
                _managedModDirectory = directory;
                _categories.Refresh(directory);
            }
            Log.Information("Regeneración: {Succeeded}. {Message}", result.Succeeded, result.Message);
        }
        catch (OperationCanceledException)
        {
            Log.Information("La regeneración fue cancelada.");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "La regeneración de la traducción falló.");
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
}
