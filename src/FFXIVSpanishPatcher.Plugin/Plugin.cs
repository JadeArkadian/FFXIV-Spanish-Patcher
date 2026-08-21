using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using FFXIVSpanishPatcher.Pipeline;
using Markdig.Syntax;
using System.Numerics;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Minimal Dalamud entrypoint used to prove shared-engine compatibility.</summary>
public sealed class Plugin : IAsyncDalamudPlugin
{
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
    private MarkdownDocument? _milestones;
    private readonly CommandInfo _openCommand;
    private bool _windowOpen;
    private bool _openMilestonesRequested;
    private bool _openHistoryRequested;
    private bool _openLegalRequested;
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

        var state = _penumbraAvailability.IsReady ? "✓ PENUMBRA LISTO" : "⦻ PENUMBRA NO DISPONIBLE";
        var stateColor = _penumbraAvailability.IsReady ? Green : Red;
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
            ImGui.TextWrapped(_controller.Snapshot.Message);
            var penumbraColor = _penumbraAvailability.IsReady ? Green : Red;
            var penumbraStatus = _penumbraAvailability.IsReady
                ? "✓ Penumbra disponible"
                : "⦻ Penumbra no disponible";
            ImGui.TextColored(penumbraColor, penumbraStatus);
            ImGui.SameLine();
            ImGui.TextDisabled(_penumbraAvailability.UserMessage);
            if (_regeneration is { } regeneration)
            {
                ImGui.TextWrapped(regeneration.Message);
            }

            if (_penumbraAvailability.Status == PenumbraAvailabilityStatus.Unavailable)
            {
                if (ImGui.SmallButton("Abrir repositorio oficial"))
                {
                    Util.OpenLink(PenumbraRepositoryUrl);
                }
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
        ImGui.EndChild();
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
            ImGui.TextWrapped(_regeneration?.Message ?? "La compatibilidad aún no se ha comprobado.");
        }

        ImGui.EndChild();
    }

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
            if (!ui.CanEditCategories || String.IsNullOrWhiteSpace(_managedModDirectory))
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
                if (DrawFramedCheckbox("Regenerar tras una actualización real", ref autoRegeneration))
                {
                    configuration.AutoRegenerationEnabled = autoRegeneration;
                    store.Save(configuration);
                    _regeneration = _currentFingerprint is null
                        ? _regeneration
                        : RegenerationPlanner.Decide(_currentFingerprint, configuration.LastSuccessfulFingerprint, autoRegeneration);
                }
            }

            ImGui.TextDisabled("Administra únicamente Base/Default; no altera otras colecciones.");
            ImGui.TextColored(Gold, "Activa Wait for plugins before game loads en Dalamud.");
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

        DrawMarkdownPopup();
        DrawHistoryPopup();
        DrawTextPopup("Aviso legal", LegalNotice.Text);
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
