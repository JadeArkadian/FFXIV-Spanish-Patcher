using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Textures;
using Dalamud.Utility;
using FFXIVSpanishPatcher.Pipeline;
using Markdig.Syntax;
using System.Numerics;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Minimal Dalamud entrypoint used to prove shared-engine compatibility.</summary>
public sealed class Plugin : IAsyncDalamudPlugin
{
    private const int PenumbraRetryLimit = 8;
    private const int PenumbraInitialRetryMilliseconds = 250;
    private const int PenumbraMaximumRetryMilliseconds = 5000;
    private static readonly Vector4 Night = new(0.027f, 0.063f, 0.118f, 0.98f);
    private static readonly Vector4 Surface = new(0.055f, 0.114f, 0.190f, 1f);
    private static readonly Vector4 SurfaceRaised = new(0.075f, 0.145f, 0.235f, 1f);
    private static readonly Vector4 Line = new(0.157f, 0.267f, 0.396f, 1f);
    private static readonly Vector4 Text = new(0.937f, 0.961f, 0.992f, 1f);
    private static readonly Vector4 Muted = new(0.557f, 0.643f, 0.749f, 1f);
    private static readonly Vector4 Blue = new(0.235f, 0.553f, 1f, 1f);
    private static readonly Vector4 Cyan = new(0.420f, 0.847f, 1f, 1f);
    private static readonly Vector4 Gold = new(0.941f, 0.741f, 0.345f, 1f);
    private static readonly Vector4 Green = new(0.345f, 0.875f, 0.651f, 1f);
    private static readonly Vector4 Red = new(0.941f, 0.353f, 0.353f, 1f);
    private const string PenumbraRepositoryUrl = "https://github.com/xivdev/Penumbra";
    private const ImGuiWindowFlags DashboardCardFlags =
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    private const ImGuiWindowFlags PopupContentFlags = ImGuiWindowFlags.AlwaysUseWindowPadding;
    private const int VisualStyleColorCount = 21;
    private const int VisualStyleVarCount = 9;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private readonly PluginLoadProbe _loadProbe = new(new DalamudGameDataProbe(DataManager));
    private readonly TranslationManagerController _controller = new(SystemPluginClock.Instance);
    private readonly TranslationRestartTracker _restartTracker = new();
    private readonly PenumbraIpcClient _penumbra = new(
        new DalamudPenumbraApiTransport(PluginInterface),
        PhysicalPenumbraDirectoryProbe.Instance,
        new DalamudPluginDiagnosticLogger(Log));
    private readonly CategorySyncService _categories = new(
        new DalamudPenumbraCategoryTransport(PluginInterface),
        new DalamudPluginDiagnosticLogger(Log));
    private readonly IGameDataBackend _buildInputs = new DalamudBuildInputProvider(typeof(Plugin).Assembly, DataManager);
    private readonly DalamudStartupWaitService _startupWaitService = new(typeof(IDalamudPluginInterface).Assembly);
    private PluginConfiguration? _configuration;
    private IPluginConfigurationStore? _configurationStore;
    private PenumbraAvailability _penumbraAvailability = new(
        PenumbraAvailabilityStatus.Unavailable, 0, 0, null,
        "Penumbra aún no se ha comprobado.");
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
    private readonly List<PluginEvent> _eventHistory = [];
    private string? _pendingInheritedCategoryDomain;
    private MarkdownDocument? _milestones;
    private readonly CommandInfo _openCommand;
    private bool _windowOpen;
    private bool _openMilestonesRequested;
    private bool _openHistoryRequested;
    private bool _openLegalRequested;
    private bool _openStartupWaitConfirmationRequested;
    private bool _openGameRestartConfirmationRequested;
    private IActiveNotification? _translationRestartNotification;
    private bool _startupWaitGuaranteedForSession;
    private DateTime _nextStartupWaitCheckUtc;
    private DateTime _nextPenumbraRetryUtc;
    private int _penumbraRetryAttempt;
    private bool _penumbraRetryPending;
    private string? _preparedSessionDirectory;
    private Task? _regenerationTask;
    private Task? _categoryUpdateTask;
    private Task? _modStateUpdateTask;

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

        RefreshStartupWait(initialCheck: true);
        Framework.Update += OnFrameworkUpdate;
        await _loadProbe.LoadAsync(cancellationToken).ConfigureAwait(false);
        _penumbra.AvailabilityChanged += OnPenumbraAvailabilityChanged;
        _penumbra.ModDeleted += OnPenumbraModDeleted;
        _penumbra.ModMoved += OnPenumbraModMoved;
        _categories.SnapshotChanged += OnCategorySnapshotChanged;
        var penumbra = await _penumbra.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        OnPenumbraAvailabilityChanged(penumbra);
        _controller.TryTransition(
            penumbra.IsReady ? PluginPhase.WelcomeRequired : PluginPhase.PenumbraUnavailable,
            penumbra.UserMessage,
            penumbra.Status == PenumbraAvailabilityStatus.Faulted ? penumbra.TechnicalError : null);
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
        if (AutomaticRegenerationPolicy.ShouldCompleteBeforeGameLoad(
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

        TryStartAutomaticRegeneration();
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
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenWindow;
        PluginInterface.UiBuilder.OpenMainUi -= OpenWindow;
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler("/ffxives");
        _penumbra.AvailabilityChanged -= OnPenumbraAvailabilityChanged;
        _penumbra.ModDeleted -= OnPenumbraModDeleted;
        _penumbra.ModMoved -= OnPenumbraModMoved;
        _categories.SnapshotChanged -= OnCategorySnapshotChanged;
        _translationRestartNotification?.DismissNow();
        _translationRestartNotification = null;
        // Stop an in-flight repair before disposing the IPC and category dependencies it uses.
        await _controller.DisposeAsync().ConfigureAwait(false);
        await _categories.DisposeAsync().ConfigureAwait(false);
        await _penumbra.DisposeAsync().ConfigureAwait(false);
        await _loadProbe.DisposeAsync().ConfigureAwait(false);
    }

    private void OnPenumbraAvailabilityChanged(PenumbraAvailability availability)
        => ApplyPenumbraAvailability(availability, restartRetryBudget: true);

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
            var identity = new PenumbraModIdentityService(_penumbra).Resolve(availability.ModDirectory);
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
        Log.Information("Categorías: {Status}. {Message}", categories.Status, categories.Message);
        ObserveTranslationRuntimeState(categories);
        var desiredEnabled = _configuration?.ManagedModEnabled ?? true;
        if (_loadCompleted
            && _penumbraAvailability.IsReady
            && ManagedModActivationPolicy.NeedsSessionRepair(categories, desiredEnabled, _regeneration))
        {
            SchedulePenumbraRetry(restartRetryBudget: true);
        }

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

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        if (now >= _nextStartupWaitCheckUtc)
        {
            _nextStartupWaitCheckUtc = now.AddSeconds(1);
            RefreshStartupWait(initialCheck: false);
        }

        if (_penumbraRetryPending && now >= _nextPenumbraRetryUtc)
        {
            _penumbraRetryPending = false;
            _penumbraRetryAttempt++;
            ApplyPenumbraAvailability(_penumbra.ProbeAvailability(), restartRetryBudget: false);
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
                PluginInterface.Reason);
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
            ShowStartupWaitNotification(current);
        }
        else if (!previous.IsReady)
        {
            TryStartAutomaticRegeneration();
        }
    }

    private void ShowStartupWaitNotification(DalamudStartupWaitSnapshot snapshot)
    {
        var (title, type) = snapshot.Status switch
        {
            DalamudStartupWaitStatus.Disabled => ("Carga temprana desactivada", NotificationType.Warning),
            DalamudStartupWaitStatus.RestartRequired => ("Reinicio necesario", NotificationType.Warning),
            _ => ("No se pudo verificar Dalamud", NotificationType.Error),
        };
        var notification = NotificationManager.AddNotification(new Notification
        {
            Title = title,
            Content = snapshot.Message,
            Type = type,
            Icon = new CrystalNotificationIcon(TextureProvider.GetFromManifestResource(
                typeof(Plugin).Assembly,
                "FFXIVSpanishTranslationManager.logo-git.png")),
            InitialDuration = TimeSpan.FromSeconds(12),
        });
        notification.Click += _ =>
        {
            _windowOpen = true;
            notification.DismissNow();
        };
    }

    private sealed class CrystalNotificationIcon(ISharedImmediateTexture texture) : INotificationIcon
    {
        private static readonly Vector2 CrystalUvMin = new(108f / 512f, 21f / 512f);
        private static readonly Vector2 CrystalUvMax = new(416f / 512f, 329f / 512f);

        public bool DrawIcon(Vector2 minCoord, Vector2 maxCoord, Vector4 color)
        {
            var wrap = texture.GetWrapOrEmpty();
            if (wrap.Handle == nint.Zero)
            {
                return false;
            }

            ImGui.GetWindowDrawList().AddImage(
                wrap.Handle,
                minCoord,
                maxCoord,
                CrystalUvMin,
                CrystalUvMax,
                uint.MaxValue);
            return true;
        }
    }

    private void OpenWindow() => _windowOpen = true;

    private void Draw()
    {
        if (!_windowOpen)
        {
            return;
        }

        PushVisualStyle();
        try
        {
            ImGui.SetNextWindowSize(new Vector2(1120, 830), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(1120, 830), new Vector2(1600, 1120));
            if (!ImGui.Begin(
                    "FFXIV Spanish Translation Manager",
                    ref _windowOpen,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                ImGui.End();
                return;
            }

            DrainEvents();
            DrawBrandHeader();
            DrawDashboard();
            DrawInformationPopups();

            ImGui.End();
        }
        finally
        {
            ImGui.PopStyleVar(VisualStyleVarCount);
            ImGui.PopStyleColor(VisualStyleColorCount);
        }
    }

    private static void PushVisualStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20f, 18f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(8f, 6f));

        ImGui.PushStyleColor(ImGuiCol.WindowBg, Night);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Line);
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Muted);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, SurfaceRaised);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, SurfaceRaised);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.086f, 0.188f, 0.318f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.125f, 0.302f, 0.518f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Blue);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.082f, 0.207f, 0.365f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.118f, 0.291f, 0.494f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Blue);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Cyan);
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.048f, 0.106f, 0.180f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.095f, 0.230f, 0.396f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.100f, 0.257f, 0.447f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Night);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Surface);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Night);
    }

    private void DrawBrandHeader()
    {
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float height = 92f;
        var end = start + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(SurfaceRaised), 12f);
        drawList.AddRect(start, end, ImGui.GetColorU32(Line), 12f);
        drawList.AddRectFilled(start, start + new Vector2(5f, height), ImGui.GetColorU32(Blue), 12f);

        ImGui.SetCursorScreenPos(start + new Vector2(18f, 16f));
        var logo = TextureProvider.GetFromManifestResource(
            typeof(Plugin).Assembly,
            "FFXIVSpanishTranslationManager.logo-git.png");
        ImGui.Image(logo.GetWrapOrEmpty().Handle, new Vector2(58f, 58f));

        ImGui.SetCursorScreenPos(start + new Vector2(92f, 17f));
        ImGui.TextColored(Cyan, "FFXIV SPANISH");
        ImGui.SameLine();
        ImGui.TextDisabled("TRANSLATION MANAGER");
        ImGui.SetCursorScreenPos(start + new Vector2(92f, 48f));
        ImGui.TextColored(Gold, "Traducción al castellano");
        ImGui.SameLine();
        ImGui.TextDisabled("· Gestión segura mediante Penumbra");

        var header = ManagedModHeaderStatusModel.Create(
            _controller.Snapshot,
            _startupWait,
            _penumbraAvailability,
            _categories.Snapshot,
            _regeneration,
            _configuration,
            _currentFingerprint,
            _managedInstallationMissing,
            _restartTracker.IsRestartRequired);
        var state = header.Label;
        var stateColor = header.Status switch
        {
            ManagedModHeaderStatus.Regenerating => Cyan,
            ManagedModHeaderStatus.Active => Green,
            ManagedModHeaderStatus.BestEffort => Gold,
            ManagedModHeaderStatus.Disabled => Muted,
            ManagedModHeaderStatus.RestartRequired => Gold,
            ManagedModHeaderStatus.Blocked or ManagedModHeaderStatus.NotInstalled => Red,
            _ => Red,
        };
        var labelSize = ImGui.CalcTextSize(state);
        var chipStart = new Vector2(end.X - labelSize.X - 34f, start.Y + 33f);
        var chipEnd = new Vector2(end.X - 14f, start.Y + 59f);
        drawList.AddRectFilled(chipStart, chipEnd, ImGui.GetColorU32(new Vector4(stateColor.X, stateColor.Y, stateColor.Z, 0.14f)), 8f);
        drawList.AddRect(chipStart, chipEnd, ImGui.GetColorU32(stateColor), 8f);
        ImGui.SetCursorScreenPos(chipStart + new Vector2(10f, 5f));
        ImGui.TextColored(stateColor, state);

        ImGui.SetCursorScreenPos(start + new Vector2(0f, height + 10f));
    }

    private void DrawDashboard()
    {
        const float topHeight = 190f;
        const float categoriesHeight = 270f;
        const float bottomHeight = 145f;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var primaryWidth = (contentWidth - gap) * 0.62f;

        DrawStatusCard(new Vector2(primaryWidth, topHeight));
        ImGui.SameLine();
        DrawCompatibilityCard(new Vector2(0f, topHeight));

        ImGui.Spacing();
        DrawCategoriesCard(new Vector2(0f, categoriesHeight));

        ImGui.Spacing();
        DrawAutomationCard(new Vector2(primaryWidth, bottomHeight));
        ImGui.SameLine();
        DrawInformationCard(new Vector2(0f, bottomHeight));
    }

    private static void DrawCardHeading(string eyebrow, string title)
    {
        ImGui.TextColored(Cyan, eyebrow.ToUpperInvariant());
        ImGui.SameLine();
        ImGui.TextUnformatted(title);
        ImGui.Separator();
    }

    private void DrawStatusCard(Vector2 size)
    {
        var ui = UiSnapshot();
        if (ImGui.BeginChild("Estado de la traducción", size, true, DashboardCardFlags))
        {
            DrawCardHeading("GESTIÓN", "Estado de la traducción");
            if (!_startupWait.IsReady)
            {
                DrawStartupWaitRequirement();
            }
            else
            {
                if (_restartTracker.IsRestartRequired)
                {
                    ImGui.TextColored(Gold, "Hay cambios pendientes de aplicar.");
                    ImGui.TextWrapped("Reinicia el juego para cargar desde el inicio el estado actual de la traducción.");
                    DrawRestartGameButton();
                }
                else if (!_penumbraAvailability.IsReady)
                {
                    ImGui.TextColored(Red, _penumbraAvailability.UserMessage);
                }
                else if (ui.CanCancelRegeneration
                    || _controller.Snapshot.Phase is PluginPhase.ErrorRecoverable
                        or PluginPhase.ErrorFatal
                        or PluginPhase.Cancelled)
                {
                    ImGui.TextWrapped(_controller.Snapshot.Message);
                }
                else if (_regeneration is { } regeneration)
                {
                    ImGui.TextWrapped(regeneration.Message);
                }

                var categories = _categories.Snapshot;
                if (_managedModDirectory is not null && categories.ModEnabled is not null)
                {
                    ImGui.Spacing();
                    var modEnabled = _configuration?.ManagedModEnabled ?? categories.ModEnabled.Value;
                    var stateUpdateRunning = _modStateUpdateTask is { IsCompleted: false };
                    ImGui.BeginDisabled(
                        stateUpdateRunning
                        || ui.CanCancelRegeneration
                        || (modEnabled == false && !categories.SessionActivationAllowed));
                    if (DrawFramedCheckbox("Traducción activa en Base/Default", ref modEnabled))
                    {
                        StartModStateUpdate(modEnabled);
                    }
                    ImGui.EndDisabled();
                    if (stateUpdateRunning)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled("Sincronizando…");
                    }
                }

                if (_penumbraAvailability.Status == PenumbraAvailabilityStatus.Unavailable)
                {
                    if (ImGui.SmallButton("Abrir repositorio oficial"))
                    {
                        Util.OpenLink(PenumbraRepositoryUrl);
                    }
                }

                ImGui.Spacing();
                if (!_restartTracker.IsRestartRequired
                    && ui.CanStartRegeneration
                    && ImGui.Button(ui.PrimaryActionLabel))
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
            }
        }
        ImGui.EndChild();
    }

    private void DrawStartupWaitRequirement()
    {
        ImGui.TextWrapped(_startupWait.Message);
        var ui = UiSnapshot();
        if (ui.CanCancelRegeneration)
        {
            ImGui.TextDisabled("La operación en curso puede terminar con seguridad o cancelarse.");
            if (ImGui.Button("Cancelar operación"))
            {
                _controller.CancelActiveOperation();
            }
            return;
        }

        if (_startupWait.CanEnable && ImGui.Button("Activar carga temprana"))
        {
            _openStartupWaitConfirmationRequested = true;
        }
        else if (_startupWait.Status == DalamudStartupWaitStatus.RestartRequired)
        {
            DrawRestartGameButton();
        }
    }

    private void DrawRestartGameButton()
    {
        ImGui.Spacing();
        if (ImGui.Button("Reiniciar juego"))
        {
            _openGameRestartConfirmationRequested = true;
        }
    }

    private void DrawCompatibilityCard(Vector2 size)
    {
        var fingerprint = _currentFingerprint ?? _configuration?.LastSuccessfulFingerprint;
        if (ImGui.BeginChild("Compatibilidad", size, true, DashboardCardFlags))
        {
            DrawCardHeading("SISTEMA", "Compatibilidad");
            DrawDetail("Plugin", fingerprint?.PluginAssemblyVersion ?? "sin datos");
            DrawDetail("FFXIV instalado", fingerprint?.InstalledGameVersion ?? "sin datos");
            DrawDetail("FFXIV recomendado", fingerprint?.RecommendedGameVersion ?? "sin datos");
            ImGui.Spacing();
            if (fingerprint is null
                || IsUnknownVersion(fingerprint.InstalledGameVersion)
                || IsUnknownVersion(fingerprint.RecommendedGameVersion))
            {
                ImGui.TextDisabled("No se ha podido comprobar la compatibilidad de FFXIV.");
            }
            else if (StringComparer.Ordinal.Equals(
                fingerprint.InstalledGameVersion,
                fingerprint.RecommendedGameVersion))
            {
                ImGui.TextColored(Green, "✓ Versión de FFXIV compatible.");
            }
            else
            {
                ImGui.TextColored(Gold, "! La versión instalada difiere de la recomendada.");
            }
        }

        ImGui.EndChild();
    }

    private static bool IsUnknownVersion(string? value)
        => String.IsNullOrWhiteSpace(value)
            || StringComparer.OrdinalIgnoreCase.Equals(value, "desconocida")
            || StringComparer.OrdinalIgnoreCase.Equals(value, "sin datos");

    private static void DrawDetail(string label, string value)
    {
        ImGui.TextDisabled($"{label}:");
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }

    private static bool DrawFramedCheckbox(string label, ref bool value)
    {
        var boxStart = ImGui.GetCursorScreenPos();
        var boxSize = ImGui.GetFrameHeight();
        var changed = ImGui.Checkbox(label, ref value);
        ImGui.GetWindowDrawList().AddRect(
            boxStart,
            boxStart + new Vector2(boxSize, boxSize),
            ImGui.GetColorU32(Line),
            ImGui.GetStyle().FrameRounding);
        return changed;
    }

    private void DrawCategoriesCard(Vector2 size)
    {
        var ui = UiSnapshot();
        var categories = _categories.Snapshot;
        if (ImGui.BeginChild("Categorías", size, true, DashboardCardFlags))
        {
            DrawCardHeading("CONTENIDO DEL MOD", "Categorías de traducción");
            ImGui.TextDisabled($"Base/Default ({categories.CollectionName}) · {categories.Message}");
            if (!_startupWait.IsReady)
            {
                ImGui.Spacing();
                ImGui.TextDisabled(_startupWait.Status switch
                {
                    DalamudStartupWaitStatus.Disabled => "Activa la carga temprana para editar las categorías.",
                    DalamudStartupWaitStatus.RestartRequired => "Las categorías estarán disponibles después de reiniciar.",
                    _ => "Las categorías no están disponibles hasta verificar la carga temprana.",
                });
            }
            else if (!ui.CanEditCategories || String.IsNullOrWhiteSpace(_managedModDirectory))
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Las categorías estarán disponibles cuando Penumbra detecte el mod gestionado.");
            }
            else if (ImGui.BeginTable("Cuadrícula de categorías", 2, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var category in TranslationCategoryCatalog.All)
                {
                    ImGui.TableNextColumn();
                    DrawCategoryToggle(category, categories);
                }

                ImGui.EndTable();
            }

            if (_pendingInheritedCategoryDomain is not null)
            {
                ImGui.TextColored(Gold, "Cambio pendiente: confirma cómo tratar la herencia.");
            }
        }

        ImGui.EndChild();
    }

    private void DrawCategoryToggle(TranslationCategoryDefinition category, CategorySyncSnapshot categories)
    {
        var enabled = categories.EnabledDomains.Contains(category.Domain);
        ImGui.PushID(category.Domain);
        var changed = DrawFramedCheckbox("##Activo", ref enabled);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(category.DisplayName);
        ImGui.EndGroup();
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(360f);
            ImGui.TextUnformatted(category.Description);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        ImGui.PopID();

        if (!changed)
        {
            return;
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

    private void DrawAutomationCard(Vector2 size)
    {
        if (ImGui.BeginChild("Automatización", size, true, DashboardCardFlags))
        {
            DrawCardHeading("AJUSTES", "Automatización");
            if (_configuration is { } configuration && _configurationStore is { } store)
            {
                var autoRegeneration = configuration.AutoRegenerationEnabled;
                if (DrawFramedCheckbox("Mantener la traducción instalada y actualizada automáticamente", ref autoRegeneration))
                {
                    configuration.AutoRegenerationEnabled = autoRegeneration;
                    store.Save(configuration);
                    RefreshRegenerationDecision();
                    TryStartAutomaticRegeneration();
                }
            }

            ImGui.TextDisabled("Administra únicamente Base/Default; no altera otras colecciones.");
        }

        ImGui.EndChild();
    }

    private void DrawInformationCard(Vector2 size)
    {
        if (ImGui.BeginChild("Información y soporte", size, true, DashboardCardFlags))
        {
            DrawCardHeading("PROYECTO", "Información y soporte");
            var gap = ImGui.GetStyle().ItemSpacing.X;
            var buttonWidth = (ImGui.GetContentRegionAvail().X - gap) / 2f;
            if (ImGui.Button("Hitos de traducción", new Vector2(buttonWidth, 34f)))
            {
                _openMilestonesRequested = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Historial de sesión", new Vector2(buttonWidth, 34f)))
            {
                _openHistoryRequested = true;
            }

            if (ImGui.Button("Aviso legal", new Vector2(buttonWidth, 34f)))
            {
                _openLegalRequested = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Copiar diagnóstico", new Vector2(buttonWidth, 34f)))
            {
                ImGui.SetClipboardText(UiSnapshot().Diagnostic);
            }
        }

        ImGui.EndChild();
    }

    private void DrawInformationPopups()
    {
        OpenRequestedPopup(ref _openMilestonesRequested, "Hitos de traducción");
        OpenRequestedPopup(ref _openHistoryRequested, "Historial de sesión");
        OpenRequestedPopup(ref _openLegalRequested, "Aviso legal");
        OpenRequestedPopup(ref _openStartupWaitConfirmationRequested, "Activar carga temprana");
        OpenRequestedPopup(ref _openGameRestartConfirmationRequested, "Confirmar reinicio del juego");

        DrawMarkdownPopup();
        DrawHistoryPopup();
        DrawTextPopup("Aviso legal", LegalNotice.Text);
        DrawStartupWaitConfirmationPopup();
        DrawGameRestartConfirmationPopup();
        DrawInheritedCategoryPopup();
    }

    private static void OpenRequestedPopup(ref bool requested, string name)
    {
        if (!requested)
        {
            return;
        }

        ImGui.OpenPopup(name);
        requested = false;
    }

    private static void DrawTextPopup(string title, string text)
    {
        ImGui.SetNextWindowSize(new Vector2(760f, 470f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup(title))
        {
            return;
        }

        ImGui.TextColored(Cyan, title.ToUpperInvariant());
        ImGui.Separator();
        if (ImGui.BeginChild(
                $"Contenido de {title}",
                new Vector2(0f, 370f),
                false,
                PopupContentFlags))
        {
            ImGui.TextWrapped(text);
        }
        ImGui.EndChild();
        if (ImGui.Button("Cerrar"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void DrawMarkdownPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(860f, 620f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("Hitos de traducción"))
        {
            return;
        }

        ImGui.TextColored(Cyan, "HITOS DE TRADUCCIÓN");
        ImGui.Separator();
        if (ImGui.BeginChild(
                "Documento de hitos",
                new Vector2(0f, 520f),
                false,
                PopupContentFlags))
        {
            if (_milestones is { } document)
            {
                MarkdownImGuiRenderer.Draw(document);
            }
            else
            {
                ImGui.TextDisabled("El historial aún no está disponible.");
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Cerrar"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void DrawHistoryPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(760f, 470f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("Historial de sesión"))
        {
            return;
        }

        ImGui.TextColored(Cyan, "HISTORIAL DE ESTA SESIÓN");
        ImGui.Separator();
        if (ImGui.BeginChild(
                "Eventos",
                new Vector2(0f, 370f),
                false,
                PopupContentFlags))
        {
            foreach (var entry in _eventHistory.TakeLast(40))
            {
                ImGui.TextWrapped($"{entry.TimestampUtc:HH:mm:ss} [{entry.Severity}] {entry.Message}");
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Cerrar"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void DrawInheritedCategoryPopup()
    {
        if (_pendingInheritedCategoryDomain is not { } pending)
        {
            return;
        }

        ImGui.OpenPopup("Confirmar categorías heredadas");
        ImGui.SetNextWindowSize(new Vector2(560f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("Confirmar categorías heredadas"))
        {
            return;
        }

        ImGui.TextWrapped("Estas categorías se heredan. Crear un ajuste propio en Base/Default dejará de heredar cambios de la colección superior.");
        if (ImGui.Button("Crear ajuste propio"))
        {
            var desired = new HashSet<string>(_categories.Snapshot.EnabledDomains, StringComparer.OrdinalIgnoreCase);
            if (!desired.Add(pending))
            {
                desired.Remove(pending);
            }

            StartCategoryUpdate(desired, confirmBreakInheritance: true);
            _pendingInheritedCategoryDomain = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancelar cambio"))
        {
            _pendingInheritedCategoryDomain = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void DrawStartupWaitConfirmationPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(600f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("Activar carga temprana"))
        {
            return;
        }

        ImGui.TextColored(Cyan, "ACTIVAR AJUSTE DE DALAMUD");
        ImGui.Separator();
        ImGui.TextWrapped(
            "Se activará Wait for plugins before game loads en la configuración de Dalamud. " +
            "El juego deberá reiniciarse para garantizar que el gestor y Penumbra carguen antes que sus recursos.");
        ImGui.Spacing();
        if (ImGui.Button("Activar y requerir reinicio"))
        {
            if (_startupWaitService.TryEnable())
            {
                RefreshStartupWait(initialCheck: false);
            }
            else
            {
                NotificationManager.AddNotification(new Notification
                {
                    Title = "No se pudo activar el ajuste",
                    Content = "Actívalo manualmente en /xlsettings → General.",
                    Type = NotificationType.Error,
                    InitialDuration = TimeSpan.FromSeconds(12),
                });
            }
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancelar"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private static void DrawGameRestartConfirmationPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(560f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(
                "Confirmar reinicio del juego",
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize))
        {
            return;
        }

        ImGui.TextColored(Gold, "REINICIAR FINAL FANTASY XIV");
        ImGui.Separator();
        ImGui.TextWrapped(
            "El cliente se cerrará inmediatamente y XIVLauncher volverá a iniciarlo. " +
            "Confirma únicamente cuando puedas abandonar la sesión actual.");
        ImGui.Spacing();
        if (ImGui.Button("Reiniciar ahora"))
        {
            if (!CommandManager.ProcessCommand("/xlrestart"))
            {
                NotificationManager.AddNotification(new Notification
                {
                    Title = "No se pudo reiniciar el juego",
                    Content = "El comando interno de reinicio de Dalamud no está disponible.",
                    Type = NotificationType.Error,
                    InitialDuration = TimeSpan.FromSeconds(12),
                });
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancelar"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private TranslationManagerUiSnapshot UiSnapshot()
        => TranslationManagerUiModel.Create(
            _controller.Snapshot,
            _startupWait,
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

    private void StartModStateUpdate(bool enabled)
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
            _categories.Refresh(directory);
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
        if (!_startupWait.IsReady || _regenerationTask is { IsCompleted: false })
        {
            return;
        }

        _regenerationTask = RegenerateAsync(
            marshalPenumbraToFrameworkThread: true,
            CancellationToken.None);
    }

    private void TryStartAutomaticRegeneration()
    {
        TryActivateVerifiedSession();
        if (_startupWait.IsReady
            && _regeneration?.RequiresBuild == true
            && _penumbraAvailability.IsReady)
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

        var desired = configuration.ManagedModEnabled ?? true;
        if (ManagedModActivationPolicy.IsSessionStateApplied(_categories.Snapshot, desired))
        {
            return true;
        }

        var snapshot = _categories.GrantSessionActivation(directory, desired);
        if (!ManagedModActivationPolicy.IsSessionStateApplied(snapshot, desired))
        {
            Log.Warning("La instalación está actualizada, pero Penumbra rechazó su estado persistente.");
            return false;
        }

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
                buildInput.InstalledGameVersion == "desconocida" ? null : buildInput.InstalledGameVersion,
                TranslationCategoryCatalog.All.Select(category => category.DisplayName),
                TranslationCategoryCatalog.All.Select(category => category.DisplayName));
            var treeBuilder = new PipelineTreeBuilder(
                pipeline,
                request => BuildPatchRequest(request, availability.ModDirectory!, metadata));
            // During a guaranteed Boot load Dalamud is deliberately holding the framework thread
            // until this method returns. Penumbra is already Ready, so its synchronous IPC must run
            // on this load worker; queueing it to Framework would deadlock startup. Every later write
            // keeps the normal framework-thread adapter.
            IPenumbraModRegistry registry = marshalPenumbraToFrameworkThread
                ? new FrameworkPenumbraModRegistry(_penumbra, Framework)
                : _penumbra;
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
                            _categories.GrantSessionActivation(previousDirectory, enableInDefaultCollection);
                        }

                        return (new RegenerationExecution(install, false),
                            PluginPhase.ErrorRecoverable,
                            install.Message);
                    }

                    var activation = _categories.GrantSessionActivation(installedDirectory, enableInDefaultCollection);
                    var activationApplied = ManagedModActivationPolicy.IsSessionStateApplied(
                        activation,
                        enableInDefaultCollection);
                    var message = activationApplied
                        ? install.Message
                        : install.Message + " El mod queda bloqueado porque Penumbra rechazó su estado persistente.";
                    return (new RegenerationExecution(install, activationApplied),
                        activationApplied ? PluginPhase.Ready : PluginPhase.ErrorRecoverable,
                        message);
                }, cancellationToken).ConfigureAwait(false);

            var result = execution.Install;

            if (result.Succeeded && result.ModDirectory is { } directory)
            {
                _managedInstallationMissing = false;
                _managedModDirectory = directory;
                if (execution.ActivationApplied)
                {
                    _preparedSessionDirectory = directory;
                }
                _categories.Refresh(directory);
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
    }

    private void ShowTranslationRestartNotification()
    {
        _translationRestartNotification?.DismissNow();
        var notification = NotificationManager.AddNotification(new Notification
        {
            Title = "Reinicio necesario",
            Content = "La traducción ya está preparada en Penumbra. Reinicia el juego para cargar sus textos desde el inicio.",
            Type = NotificationType.Warning,
            Icon = new CrystalNotificationIcon(TextureProvider.GetFromManifestResource(
                typeof(Plugin).Assembly,
                "FFXIVSpanishTranslationManager.logo-git.png")),
            InitialDuration = TimeSpan.FromSeconds(12),
        });
        notification.Click += _ =>
        {
            _windowOpen = true;
            notification.DismissNow();
        };
        _translationRestartNotification = notification;
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
            ShowTranslationRestartNotification();
        }
        else
        {
            _translationRestartNotification?.DismissNow();
            _translationRestartNotification = null;
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
