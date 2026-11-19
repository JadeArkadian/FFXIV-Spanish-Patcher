using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// The manager's ImGui window. It shows the session's state and forwards the user's clicks to it;
/// it performs no IPC, filesystem or regeneration work itself.
/// </summary>
/// <remarks>
/// Layout rule: nothing has a hand-picked height. The window sizes itself to its content and every
/// card wraps exactly what it holds, so each view (dashboard, missing Penumbra, pending restart)
/// is as large as it needs and no larger, at any interface scale.
/// </remarks>
internal sealed class TranslationManagerWindow : IDisposable
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
    private const string PenumbraRepositoryUrl = "https://raw.githubusercontent.com/xivdev/Penumbra/master/repo.json";
    private const string LogoResource = "FFXIVSpanishTranslationManager.logo-git.png";
    private const ImGuiWindowFlags PopupContentFlags = ImGuiWindowFlags.AlwaysUseWindowPadding;
    private const int VisualStyleColorCount = 21;
    private const int VisualStyleVarCount = 9;

    // The logo file is the crystal over a wordmark. At header size the wordmark is unreadable, so
    // the header shows only the crystal (this square of the 512 px image) and sets the name in text.
    private static readonly Vector2 CrystalUvMin = new(108f / 512f, 21f / 512f);
    private static readonly Vector2 CrystalUvMax = new(416f / 512f, 329f / 512f);

    // Design width of the content column, before the interface scale.
    private const float ContentWidth = 740f;

    private readonly DalamudServices _dalamud;
    private readonly TranslationManagerSession _session;
    private readonly IFontHandle _titleFont;
    private readonly IFontHandle _largeIconFont;
    private readonly List<PluginEvent> _eventHistory = [];
    private string? _pendingInheritedCategoryDomain;
    private bool _windowOpen;
    private bool _openMilestonesRequested;
    private bool _openHistoryRequested;
    private bool _openLegalRequested;
    private bool _openStartupWaitConfirmationRequested;
    private bool _openGameRestartConfirmationRequested;
    private Vector2 _cardStart;
    private float _cardWidth;
    private Vector4? _cardAccent;
    private Vector4 _cardBackground;

    public TranslationManagerWindow(DalamudServices dalamud, TranslationManagerSession session)
    {
        _dalamud = dalamud;
        _session = session;
        // Both follow Dalamud's font and interface scale. Until they are built, Push() keeps the
        // current font, so the window is usable from its first frame.
        var atlas = dalamud.PluginInterface.UiBuilder.FontAtlas;
        _titleFont = atlas.NewDelegateFontHandle(build => build.OnPreBuild(toolkit =>
            toolkit.AddDalamudDefaultFont(-1.45f)));
        _largeIconFont = atlas.NewDelegateFontHandle(build => build.OnPreBuild(toolkit =>
            toolkit.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = UiBuilder.DefaultFontSizePx * 1.7f })));
    }

    private IDalamudPluginInterface PluginInterface => _dalamud.PluginInterface;

    private ICommandManager CommandManager => _dalamud.CommandManager;

    private ITextureProvider TextureProvider => _dalamud.TextureProvider;

    private INotificationManager NotificationManager => _dalamud.NotificationManager;

    private IFontHandle IconFont => PluginInterface.UiBuilder.IconFontFixedWidthHandle;

    private static float Scale => ImGuiHelpers.GlobalScale;

    private static float FullWidth => ContentWidth * Scale;

    private static Vector2 CardPadding => new Vector2(18f, 14f) * Scale;

    public void Open() => _windowOpen = true;

    public void Dispose()
    {
        _titleFont.Dispose();
        _largeIconFont.Dispose();
    }

    public void Draw()
    {
        if (!_windowOpen)
        {
            return;
        }

        PushVisualStyle();
        try
        {
            // The window is exactly as large as the view it shows. Only a screen smaller than the
            // content caps it, and then it scrolls.
            ImGui.SetNextWindowSizeConstraints(Vector2.Zero, ImGui.GetMainViewport().WorkSize);
            if (!ImGui.Begin(
                    "FFXIV Spanish Translation Manager",
                    ref _windowOpen,
                    ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.End();
                return;
            }

            DrainEvents();
            DrawHeader();
            // A working IPC connection always wins over the plugin list, which can hold a
            // second, unloaded Penumbra entry (dev + repository installs).
            if (!_session.PenumbraAvailability.IsReady
                && (!_session.IsPenumbraInstalled || !_session.IsPenumbraLoaded))
            {
                DrawPenumbraDependency();
            }
            else
            {
                DrawDashboard();
            }

            DrawFooter();
            DrawInformationPopups();

            ImGui.End();
        }
        finally
        {
            ImGui.PopStyleVar(VisualStyleVarCount);
            ImGui.PopStyleColor(VisualStyleColorCount);
        }
    }

    private static Vector2 PopupSize(float width, float height)
        => Vector2.Min(new Vector2(width, height) * Scale, ImGui.GetMainViewport().WorkSize * 0.9f);

    private static void PushVisualStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 8f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 8f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20f, 18f) * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12f, 8f) * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 10f) * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(8f, 6f) * Scale);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, Night);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Line);
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Muted);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Night);
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

    // ---------------------------------------------------------------- building blocks

    /// <summary>
    /// Opens a card that will be exactly as tall as its content. The content is drawn first, on
    /// an upper draw channel; <see cref="EndCard"/> then measures it and paints the panel behind.
    /// Cards do not nest.
    /// </summary>
    private void BeginCard(Vector4? accent = null, Vector4? background = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        _cardStart = ImGui.GetCursorScreenPos();
        _cardWidth = FullWidth;
        _cardAccent = accent;
        _cardBackground = background ?? Surface;
        ImGui.SetCursorScreenPos(_cardStart + CardPadding);
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + CardInnerWidth);
    }

    private void EndCard()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var end = new Vector2(_cardStart.X + _cardWidth, ImGui.GetItemRectMax().Y + CardPadding.Y);
        var rounding = 12f * Scale;
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(_cardStart, end, ImGui.GetColorU32(_cardBackground), rounding);
        if (_cardAccent is { } accent)
        {
            // A state-coloured edge: the card's meaning is visible before any word is read.
            drawList.AddRectFilled(
                _cardStart,
                new Vector2(_cardStart.X + (5f * Scale), end.Y),
                ImGui.GetColorU32(accent),
                rounding,
                ImDrawFlags.RoundCornersLeft);
        }

        drawList.AddRect(_cardStart, end, ImGui.GetColorU32(Line), rounding);
        drawList.ChannelsMerge();
        // A zero-height item spanning the card tells ImGui how wide and tall the panel really is.
        ImGui.SetCursorScreenPos(new Vector2(_cardStart.X, end.Y));
        ImGui.Dummy(new Vector2(_cardWidth, 0f));
    }

    private static float CardInnerWidth => FullWidth - (CardPadding.X * 2f);

    private void DrawIcon(FontAwesomeIcon icon, Vector4 color)
    {
        using (IconFont.Push())
        {
            ImGui.TextColored(color, icon.ToIconString());
        }
    }

    private void DrawCardTitle(FontAwesomeIcon icon, string title, string? aside = null)
    {
        var left = ImGui.GetCursorPosX();
        DrawIcon(icon, Cyan);
        ImGui.SameLine(0f, 8f * Scale);
        ImGui.TextUnformatted(title);
        if (aside is not null)
        {
            var width = ImGui.CalcTextSize(aside).X;
            ImGui.SameLine();
            // Right-aligned when it fits beside the title; otherwise it simply follows it.
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), left + CardInnerWidth - width));
            ImGui.TextDisabled(aside);
        }

        ImGui.Spacing();
    }

    /// <summary>A button with an icon and a label centred together, optionally emphasised.</summary>
    private bool IconTextButton(FontAwesomeIcon icon, string label, float width = 0f, bool primary = false)
    {
        var iconText = icon.ToIconString();
        Vector2 iconSize;
        using (IconFont.Push())
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var labelSize = ImGui.CalcTextSize(label);
        var spacing = 8f * Scale;
        var contentWidth = iconSize.X + spacing + labelSize.X;
        var size = new Vector2(
            width > 0f ? width : contentWidth + (ImGui.GetStyle().FramePadding.X * 2f),
            ImGui.GetFrameHeight());
        if (primary)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.145f, 0.388f, 0.816f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Blue);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.369f, 0.651f, 1f, 1f));
        }

        var clicked = ImGui.Button("##" + label, size);
        if (primary)
        {
            ImGui.PopStyleColor(3);
        }

        var min = ImGui.GetItemRectMin();
        var color = ImGui.GetColorU32(ImGuiCol.Text);
        var x = min.X + ((size.X - contentWidth) / 2f);
        var drawList = ImGui.GetWindowDrawList();
        using (IconFont.Push())
        {
            drawList.AddText(new Vector2(x, min.Y + ((size.Y - iconSize.Y) / 2f)), color, iconText);
        }

        drawList.AddText(
            new Vector2(x + iconSize.X + spacing, min.Y + ((size.Y - labelSize.Y) / 2f)),
            color,
            label);
        return clicked;
    }

    private static void DrawWrapped(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
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

    private static Vector4 ToneColor(StatusTone tone) => tone switch
    {
        StatusTone.Error => Red,
        StatusTone.Warning => Gold,
        _ => Text,
    };

    private static (FontAwesomeIcon Icon, Vector4 Color) StatusVisual(ManagedModHeaderStatus status) => status switch
    {
        ManagedModHeaderStatus.Active => (FontAwesomeIcon.CheckCircle, Green),
        ManagedModHeaderStatus.BestEffort => (FontAwesomeIcon.CheckCircle, Gold),
        ManagedModHeaderStatus.Regenerating => (FontAwesomeIcon.SyncAlt, Cyan),
        ManagedModHeaderStatus.RestartRequired => (FontAwesomeIcon.PowerOff, Gold),
        ManagedModHeaderStatus.Disabled => (FontAwesomeIcon.PauseCircle, Muted),
        ManagedModHeaderStatus.NotInstalled => (FontAwesomeIcon.Download, Cyan),
        _ => (FontAwesomeIcon.ExclamationTriangle, Red),
    };

    // ---------------------------------------------------------------- header and footer

    private void DrawHeader()
    {
        BeginCard(background: SurfaceRaised);
        var emblem = 60f * Scale;
        var logo = TextureProvider.GetFromManifestResource(typeof(Plugin).Assembly, LogoResource);
        ImGui.Image(logo.GetWrapOrEmpty().Handle, new Vector2(emblem, emblem), CrystalUvMin, CrystalUvMax);

        ImGui.SameLine(0f, 16f * Scale);
        var top = ImGui.GetCursorPosY();
        float titleHeight;
        using (_titleFont.Push())
        {
            titleHeight = ImGui.GetTextLineHeight();
        }

        // The two text lines are centred on the emblem.
        var block = titleHeight + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeight();
        ImGui.SetCursorPosY(top + MathF.Max(0f, (emblem - block) / 2f));
        ImGui.BeginGroup();
        using (_titleFont.Push())
        {
            ImGui.TextColored(Cyan, "FFXIV");
            ImGui.SameLine(0f, 8f * Scale);
            ImGui.TextUnformatted("EN ESPAÑOL");
        }

        ImGui.TextColored(Gold, "Traducción al castellano");
        ImGui.SameLine(0f, 6f * Scale);
        ImGui.TextDisabled("· gestionada mediante Penumbra");
        ImGui.EndGroup();
        EndCard();
    }

    private void DrawFooter()
    {
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var width = MathF.Floor((FullWidth - (gap * 3f)) / 4f);
        if (IconTextButton(FontAwesomeIcon.Scroll, "Hitos de traducción", width))
        {
            _openMilestonesRequested = true;
        }

        ImGui.SameLine();
        if (IconTextButton(FontAwesomeIcon.History, "Historial de sesión", width))
        {
            _openHistoryRequested = true;
        }

        ImGui.SameLine();
        if (IconTextButton(FontAwesomeIcon.BalanceScale, "Aviso legal", width))
        {
            _openLegalRequested = true;
        }

        ImGui.SameLine();
        if (IconTextButton(FontAwesomeIcon.Clipboard, "Copiar diagnóstico", width))
        {
            ImGui.SetClipboardText(_session.UiSnapshot().Diagnostic);
        }
    }

    // ---------------------------------------------------------------- views

    private void DrawDashboard()
    {
        var ui = _session.UiSnapshot();
        DrawStatusCard(ui);
        // Without an installed mod, or before the early-load requirement is met, there are no
        // categories to act on; the status card already says why.
        if (_session.StartupWait.IsReady && !String.IsNullOrWhiteSpace(_session.ManagedModDirectory))
        {
            DrawCategoriesCard(ui);
        }

        DrawAutomationCard();
        DrawCompatibilityCard();
    }

    private void DrawPenumbraDependency()
    {
        BeginCard(accent: Gold);
        using (_largeIconFont.Push())
        {
            ImGui.TextColored(Gold, FontAwesomeIcon.Plug.ToIconString());
        }

        ImGui.SameLine(0f, 14f * Scale);
        ImGui.BeginGroup();
        if (!_session.IsPenumbraInstalled)
        {
            using (_titleFont.Push())
            {
                ImGui.TextUnformatted("Penumbra es necesario");
            }

            DrawWrapped(
                Muted,
                "Este gestor instala y administra la traducción mediante Penumbra. Son dos pasos y solo hay que hacerlos una vez.");

            ImGui.Spacing();
            ImGui.TextColored(Cyan, "1 · Añade el repositorio oficial de Penumbra");
            ImGui.TextWrapped("Copia este enlace y pégalo en los repositorios personalizados de Dalamud.");
            ImGui.TextDisabled(PenumbraRepositoryUrl);
            if (IconTextButton(FontAwesomeIcon.Copy, "Copiar enlace"))
            {
                ImGui.SetClipboardText(PenumbraRepositoryUrl);
            }

            ImGui.SameLine();
            if (IconTextButton(FontAwesomeIcon.Cog, "Abrir ajustes de Dalamud"))
            {
                PluginInterface.OpenDalamudSettingsTo(SettingsOpenKind.Experimental);
            }

            ImGui.Spacing();
            ImGui.TextColored(Cyan, "2 · Instala Penumbra");
            ImGui.TextWrapped("Con el repositorio añadido, búscalo en el instalador de plugins y pulsa Instalar.");
            if (IconTextButton(FontAwesomeIcon.Search, "Buscar Penumbra en el instalador", primary: true))
            {
                PluginInterface.OpenPluginInstallerTo(
                    PluginInstallerOpenKind.AllPlugins,
                    TranslationManagerSession.PenumbraInternalName);
            }

            ImGui.SameLine();
            if (IconTextButton(FontAwesomeIcon.SyncAlt, "Comprobar de nuevo"))
            {
                _session.RecheckPenumbra();
            }

        }
        else
        {
            using (_titleFont.Push())
            {
                ImGui.TextUnformatted("Penumbra no está cargado");
            }

            DrawWrapped(
                Muted,
                _session.IsPenumbraOutdated
                    ? "Dalamud detecta Penumbra instalado, pero su versión no es compatible con la API actual. Actualízalo desde el instalador de plugins."
                    : "Penumbra está instalado, pero no aparece cargado en esta sesión. Este gestor no puede activarlo; habilítalo desde sus controles en Dalamud.");
            ImGui.Spacing();
            if (IconTextButton(FontAwesomeIcon.Plug, "Abrir Penumbra en plugins instalados", primary: true))
            {
                PluginInterface.OpenPluginInstallerTo(
                    PluginInstallerOpenKind.InstalledPlugins,
                    TranslationManagerSession.PenumbraInternalName);
            }

            ImGui.SameLine();
            if (IconTextButton(FontAwesomeIcon.SyncAlt, "Volver a comprobar"))
            {
                _session.RecheckPenumbra();
            }

            ImGui.TextDisabled("En cuanto Penumbra cargue, esta pantalla se actualiza sola.");
        }

        ImGui.EndGroup();
        EndCard();
    }

    // ---------------------------------------------------------------- status

    private void DrawStatusCard(TranslationManagerUiSnapshot ui)
    {
        var header = _session.HeaderSnapshot();
        var (icon, color) = StatusVisual(header.Status);
        BeginCard(accent: color);

        var actionWidth = 210f * Scale;
        var columnGap = 18f * Scale;
        var left = ImGui.GetCursorPosX();
        var rightColumn = left + CardInnerWidth - actionWidth;

        // Left: what is going on, in one headline and one sentence.
        ImGui.BeginGroup();
        using (_largeIconFont.Push())
        {
            ImGui.TextColored(color, icon.ToIconString());
        }

        ImGui.SameLine(0f, 14f * Scale);
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(rightColumn - columnGap);
        using (_titleFont.Push())
        {
            ImGui.TextColored(color, header.Label);
        }

        DrawStatusBody(ui, rightColumn - columnGap - ImGui.GetCursorPosX());
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.EndGroup();

        // Right: what can be done about it.
        ImGui.SameLine();
        ImGui.SetCursorPosX(rightColumn);
        ImGui.BeginGroup();
        DrawStatusActions(ui, actionWidth);
        ImGui.EndGroup();
        EndCard();
    }

    private void DrawStatusBody(TranslationManagerUiSnapshot ui, float width)
    {
        if (ui.CanCancelRegeneration)
        {
            // Live pipeline step: a multi-second build must never look frozen.
            DrawWrapped(Muted, _session.GenerationProgress ?? _session.State.Message);
            DrawActivityBar(width);
            return;
        }

        if (!_session.StartupWait.IsReady)
        {
            ImGui.TextWrapped(_session.StartupWait.Message);
            DrawWrapped(Muted, _session.StartupWait.Status switch
            {
                DalamudStartupWaitStatus.Disabled => "Las categorías se podrán editar al activar la carga temprana.",
                DalamudStartupWaitStatus.RestartRequired => "Las categorías estarán disponibles después de reiniciar.",
                _ => "Las categorías no están disponibles hasta verificar la carga temprana.",
            });
            return;
        }

        if (!_session.PenumbraAvailability.IsReady)
        {
            ImGui.TextWrapped(_session.PenumbraAvailability.UserMessage);
            return;
        }

        // Outcome of the last operation, missing installation, pending restart or the plain
        // regeneration summary: the model picks the one that matters most.
        DrawWrapped(ui.StatusTone == StatusTone.Neutral ? Muted : ToneColor(ui.StatusTone), ui.StatusMessage);

        var categories = _session.Categories;
        if (_session.ManagedModDirectory is null || categories.ModEnabled is null)
        {
            return;
        }

        ImGui.Spacing();
        var modEnabled = _session.Configuration?.ManagedModEnabled ?? categories.ModEnabled.Value;
        var stateUpdateRunning = _session.IsModStateUpdateRunning;
        ImGui.BeginDisabled(stateUpdateRunning || (modEnabled == false && !categories.SessionActivationAllowed));
        if (DrawFramedCheckbox("Traducción activa", ref modEnabled))
        {
            _session.StartModStateUpdate(modEnabled);
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled(stateUpdateRunning ? "Sincronizando…" : "en Base/Default");
    }

    private void DrawStatusActions(TranslationManagerUiSnapshot ui, float width)
    {
        if (ui.CanCancelRegeneration)
        {
            if (IconTextButton(FontAwesomeIcon.Stop, "Cancelar", width))
            {
                _session.CancelActiveOperation();
            }

            return;
        }

        if (!_session.StartupWait.IsReady)
        {
            if (_session.StartupWait.CanEnable)
            {
                if (IconTextButton(FontAwesomeIcon.Magic, "Activar carga temprana", width, primary: true))
                {
                    _openStartupWaitConfirmationRequested = true;
                }
            }
            else if (_session.StartupWait.Status == DalamudStartupWaitStatus.RestartRequired)
            {
                DrawRestartGameButton(width, primary: true);
            }

            return;
        }

        if (!_session.PenumbraAvailability.IsReady)
        {
            if (_session.PenumbraAvailability.Status is PenumbraAvailabilityStatus.Disabled
                or PenumbraAvailabilityStatus.IncompatibleBreakingVersion
                or PenumbraAvailabilityStatus.InsufficientFeatureVersion)
            {
                if (IconTextButton(FontAwesomeIcon.Plug, "Abrir Penumbra", width, primary: true))
                {
                    PluginInterface.OpenPluginInstallerTo(
                        PluginInstallerOpenKind.InstalledPlugins,
                        TranslationManagerSession.PenumbraInternalName);
                }
            }
            else if (IconTextButton(FontAwesomeIcon.SyncAlt, "Reintentar conexión", width, primary: true))
            {
                _session.RetryPenumbraNow();
            }

            return;
        }

        // A pending restart is the thing to do; otherwise the action is emphasised only when the
        // translation actually needs it. Regenerating stays available either way: after a
        // cancelled or failed operation it is the only way to finish the job.
        if (ui.CanRestartGame)
        {
            DrawRestartGameButton(width, primary: true);
        }

        if (ui.CanStartRegeneration
            && IconTextButton(
                ui.ActionRecommended ? FontAwesomeIcon.Download : FontAwesomeIcon.Redo,
                ui.PrimaryActionLabel,
                width,
                primary: ui.ActionRecommended && !ui.CanRestartGame))
        {
            _session.StartRegeneration(requestedByUser: true);
        }
    }

    private void DrawRestartGameButton(float width, bool primary)
    {
        if (IconTextButton(FontAwesomeIcon.PowerOff, "Reiniciar juego", width, primary))
        {
            _openGameRestartConfirmationRequested = true;
        }
    }

    /// <summary>An indeterminate bar: the pipeline reports steps, not a percentage.</summary>
    private static void DrawActivityBar(float width)
    {
        var size = new Vector2(MathF.Max(width, 1f), 5f * Scale);
        var start = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(start, start + size, ImGui.GetColorU32(Line), size.Y / 2f);
        var chunk = size.X * 0.28f;
        var travel = (float)(ImGui.GetTime() % 1.6 / 1.6) * (size.X + chunk);
        var from = MathF.Max(start.X, start.X + travel - chunk);
        var to = MathF.Min(start.X + size.X, start.X + travel);
        if (to > from)
        {
            drawList.AddRectFilled(
                new Vector2(from, start.Y),
                new Vector2(to, start.Y + size.Y),
                ImGui.GetColorU32(Cyan),
                size.Y / 2f);
        }

        ImGui.Dummy(size);
    }

    // ---------------------------------------------------------------- categories

    private void DrawCategoriesCard(TranslationManagerUiSnapshot ui)
    {
        var categories = _session.Categories;
        BeginCard();
        if (!ui.CanEditCategories)
        {
            // Not editable right now: say why instead of showing switches that would do nothing.
            DrawCardTitle(FontAwesomeIcon.LayerGroup, "Categorías de traducción");
            DrawWrapped(
                ui.CanCancelRegeneration ? Muted : Gold,
                ui.CanCancelRegeneration
                    ? "Las categorías se podrán editar cuando termine la operación en curso."
                    : categories.Message);
            EndCard();
            return;
        }

        DrawCardTitle(FontAwesomeIcon.LayerGroup, "Categorías de traducción", categories.Message);
        var left = ImGui.GetCursorPosX();
        var columnWidth = CardInnerWidth / 2f;
        for (var index = 0; index < TranslationCategoryCatalog.All.Count; index++)
        {
            if (index % 2 == 1)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(left + columnWidth);
            }

            DrawCategoryToggle(TranslationCategoryCatalog.All[index], categories);
        }

        ImGui.Spacing();
        DrawWrapped(
            _pendingInheritedCategoryDomain is null ? Muted : Gold,
            _pendingInheritedCategoryDomain is null
                ? "Pasa el cursor sobre una categoría para ver qué textos incluye."
                : "Cambio pendiente: confirma cómo tratar la herencia.");
        EndCard();
    }

    private void DrawCategoryToggle(TranslationCategoryDefinition category, CategorySyncSnapshot categories)
    {
        var enabled = categories.EnabledDomains.Contains(category.Domain);
        ImGui.PushID(category.Domain);
        var changed = DrawFramedCheckbox(category.DisplayName, ref enabled);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(360f * Scale);
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
            _session.StartCategoryUpdate(desired, confirmBreakInheritance: false);
        }
    }

    // ---------------------------------------------------------------- settings and compatibility

    private void DrawAutomationCard()
    {
        BeginCard();
        DrawCardTitle(FontAwesomeIcon.Cog, "Automatización");
        if (_session.IsConfigurationReadOnly && _session.Configuration is { } futureConfiguration)
        {
            DrawWrapped(
                Gold,
                $"Configuración v{futureConfiguration.Version}: se usarán los ajustes conocidos, pero los ajustes del plugin no se guardarán. Actualízalo para volver a editarlos.");
        }

        if (_session.Configuration is { } configuration)
        {
            var autoRegeneration = configuration.AutoRegenerationEnabled;
            ImGui.BeginDisabled(_session.IsConfigurationReadOnly);
            if (DrawFramedCheckbox("Mantener la traducción instalada y actualizada automáticamente", ref autoRegeneration))
            {
                _session.SetAutomaticRegeneration(autoRegeneration);
            }

            ImGui.EndDisabled();
        }

        DrawWrapped(Muted, "Administra únicamente Base/Default; no altera otras colecciones.");
        EndCard();
    }

    private void DrawCompatibilityCard()
    {
        var fingerprint = _session.CurrentFingerprint ?? _session.Configuration?.LastSuccessfulFingerprint;
        var installed = fingerprint?.InstalledGameVersion;
        var recommended = fingerprint?.RecommendedGameVersion;
        var unknown = fingerprint is null
            || GameVersionText.IsUnknown(installed)
            || GameVersionText.IsUnknown(recommended);
        var matches = !unknown && StringComparer.Ordinal.Equals(installed, recommended);

        BeginCard();
        if (unknown)
        {
            DrawIcon(FontAwesomeIcon.InfoCircle, Muted);
            ImGui.SameLine(0f, 8f * Scale);
            ImGui.TextDisabled("No se ha podido comprobar la versión de FFXIV.");
        }
        else if (matches)
        {
            DrawIcon(FontAwesomeIcon.CheckCircle, Green);
            ImGui.SameLine(0f, 8f * Scale);
            ImGui.TextColored(Green, "Versión de FFXIV compatible");
        }
        else
        {
            DrawIcon(FontAwesomeIcon.ExclamationTriangle, Gold);
            ImGui.SameLine(0f, 8f * Scale);
            ImGui.TextColored(Gold, "La versión instalada difiere de la recomendada");
        }

        // The numbers are reference material: one quiet line, both versions only when they differ.
        var details = $"Plugin {fingerprint?.PluginAssemblyVersion ?? "sin datos"}  ·  FFXIV {installed ?? "sin datos"}";
        if (!unknown && !matches)
        {
            details += $"  ·  recomendada {recommended}";
        }

        DrawWrapped(Muted, details);
        EndCard();
    }

    // ---------------------------------------------------------------- popups

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
        ImGui.SetNextWindowSize(PopupSize(760f, 470f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup(title))
        {
            return;
        }

        ImGui.TextColored(Cyan, title.ToUpperInvariant());
        ImGui.Separator();
        if (ImGui.BeginChild(
                $"Contenido de {title}",
                new Vector2(0f, -ImGui.GetFrameHeightWithSpacing()),
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
        ImGui.SetNextWindowSize(PopupSize(860f, 620f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("Hitos de traducción"))
        {
            return;
        }

        ImGui.TextColored(Cyan, "HITOS DE TRADUCCIÓN");
        ImGui.Separator();
        if (ImGui.BeginChild(
                "Documento de hitos",
                new Vector2(0f, -ImGui.GetFrameHeightWithSpacing()),
                false,
                PopupContentFlags))
        {
            if (_session.Milestones is { } document)
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
        ImGui.SetNextWindowSize(PopupSize(760f, 470f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("Historial de sesión"))
        {
            return;
        }

        ImGui.TextColored(Cyan, "HISTORIAL DE ESTA SESIÓN");
        ImGui.Separator();
        if (ImGui.BeginChild(
                "Eventos",
                new Vector2(0f, -ImGui.GetFrameHeightWithSpacing()),
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
        ImGui.SetNextWindowSize(PopupSize(560f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("Confirmar categorías heredadas"))
        {
            return;
        }

        ImGui.TextWrapped("Estas categorías se heredan. Crear un ajuste propio en Base/Default dejará de heredar cambios de la colección superior.");
        if (ImGui.Button("Crear ajuste propio"))
        {
            var desired = new HashSet<string>(_session.Categories.EnabledDomains, StringComparer.OrdinalIgnoreCase);
            if (!desired.Add(pending))
            {
                desired.Remove(pending);
            }

            _session.StartCategoryUpdate(desired, confirmBreakInheritance: true);
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
        ImGui.SetNextWindowSize(PopupSize(600f, 0f), ImGuiCond.Appearing);
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
            if (!_session.TryEnableStartupWait())
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

    private void DrawGameRestartConfirmationPopup()
    {
        ImGui.SetNextWindowSize(PopupSize(560f, 0f), ImGuiCond.Appearing);
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

    private void DrainEvents()
    {
        _eventHistory.AddRange(_session.DrainEvents());
        if (_eventHistory.Count > 128)
        {
            _eventHistory.RemoveRange(0, _eventHistory.Count - 128);
        }
    }
}
