using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// The manager's ImGui window: visual style, layout and popups. It shows the session's state and
/// forwards the user's clicks to it; it performs no IPC, filesystem or regeneration work itself.
/// </summary>
internal sealed class TranslationManagerWindow(DalamudServices dalamud, TranslationManagerSession session)
{
    private readonly TranslationManagerSession _session = session;

    private IDalamudPluginInterface PluginInterface => dalamud.PluginInterface;

    private ICommandManager CommandManager => dalamud.CommandManager;

    private ITextureProvider TextureProvider => dalamud.TextureProvider;

    private INotificationManager NotificationManager => dalamud.NotificationManager;

    public void Open() => _windowOpen = true;

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

    // Cards are sized from font metrics; a scrollbar appears only if a rare long message overflows,
    // so a control is never clipped out of reach.
    private const ImGuiWindowFlags DashboardCardFlags = ImGuiWindowFlags.None;

    private const ImGuiWindowFlags PopupContentFlags = ImGuiWindowFlags.AlwaysUseWindowPadding;

    private const int VisualStyleColorCount = 21;

    private const int VisualStyleVarCount = 9;

    private readonly List<PluginEvent> _eventHistory = [];

    private string? _pendingInheritedCategoryDomain;

    private bool _windowOpen;

    private bool _openMilestonesRequested;

    private bool _openHistoryRequested;

    private bool _openLegalRequested;

    private bool _openStartupWaitConfirmationRequested;

    private bool _openGameRestartConfirmationRequested;

    public void Draw()
    {
        if (!_windowOpen)
        {
            return;
        }

        PushVisualStyle();
        try
        {
            // The window is fitted to its content: the minimum shows every card whole, and the
            // maximum stops short of the stretched, half-empty layout a free resize produced.
            // Only a screen smaller than the content makes it scroll.
            var screen = ImGui.GetMainViewport().WorkSize;
            var contentHeight = MathF.Ceiling(DashboardMetrics.Measure().WindowHeight);
            var minimumSize = Vector2.Min(new Vector2(900f * Scale, contentHeight), screen);
            var maximumSize = Vector2.Max(
                minimumSize,
                Vector2.Min(new Vector2(1200f * Scale, contentHeight + (48f * Scale)), screen));
            ImGui.SetNextWindowSize(
                Vector2.Min(new Vector2(1016f * Scale, contentHeight + (32f * Scale)), screen),
                ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(minimumSize, maximumSize);
            if (!ImGui.Begin("FFXIV Spanish Translation Manager", ref _windowOpen))
            {
                ImGui.End();
                return;
            }

            DrainEvents();
            DrawBrandHeader();
            // A working IPC connection always wins over the plugin list, which can hold a
            // second, unloaded Penumbra entry (dev + repository installs).
            if (!_session.PenumbraAvailability.IsReady && (!_session.IsPenumbraInstalled || !_session.IsPenumbraLoaded))
            {
                DrawPenumbraDependencyScreen();
            }
            else
            {
                DrawDashboard();
            }

            DrawInformationPopups();

            ImGui.End();
        }
        finally
        {
            ImGui.PopStyleVar(VisualStyleVarCount);
            ImGui.PopStyleColor(VisualStyleColorCount);
        }
    }

    private static float Scale => ImGuiHelpers.GlobalScale;

    private static Vector2 Scaled(float x, float y) => new Vector2(x, y) * Scale;

    private static Vector2 PopupSize(float width, float height)
        => Vector2.Min(Scaled(width, height), ImGui.GetMainViewport().WorkSize * 0.9f);

    private static void DrawWrapped(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private static void PushVisualStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 8f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 8f * Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Scaled(20f, 18f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Scaled(12f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Scaled(10f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, Scaled(8f, 6f));

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
        var height = 92f * Scale;
        var rounding = 12f * Scale;
        var end = start + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(SurfaceRaised), rounding);
        drawList.AddRect(start, end, ImGui.GetColorU32(Line), rounding);
        drawList.AddRectFilled(start, start + new Vector2(5f * Scale, height), ImGui.GetColorU32(Blue), rounding);

        ImGui.SetCursorScreenPos(start + Scaled(18f, 16f));
        var logo = TextureProvider.GetFromManifestResource(
            typeof(Plugin).Assembly,
            "FFXIVSpanishTranslationManager.logo-git.png");
        ImGui.Image(logo.GetWrapOrEmpty().Handle, Scaled(58f, 58f));

        ImGui.SetCursorScreenPos(start + Scaled(92f, 17f));
        ImGui.TextColored(Cyan, "FFXIV SPANISH");
        ImGui.SameLine();
        ImGui.TextDisabled("TRANSLATION MANAGER");
        ImGui.SetCursorScreenPos(start + Scaled(92f, 48f));
        ImGui.TextColored(Gold, "Traducción al castellano");
        ImGui.SameLine();
        ImGui.TextDisabled("· Gestión segura mediante Penumbra");

        var header = _session.HeaderSnapshot();
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
        // The chip hugs its label vertically, so it stays centred at every font scale.
        var chipPadding = Scaled(10f, 5f);
        var chipEnd = new Vector2(end.X - 14f * Scale, start.Y + (height + labelSize.Y) / 2f + chipPadding.Y);
        var chipStart = chipEnd - labelSize - chipPadding * 2f;
        drawList.AddRectFilled(chipStart, chipEnd, ImGui.GetColorU32(new Vector4(stateColor.X, stateColor.Y, stateColor.Z, 0.14f)), 8f * Scale);
        drawList.AddRect(chipStart, chipEnd, ImGui.GetColorU32(stateColor), 8f * Scale);
        ImGui.SetCursorScreenPos(chipStart + chipPadding);
        ImGui.TextColored(stateColor, state);

        ImGui.SetCursorScreenPos(start + new Vector2(0f, HeaderBlockHeight));
    }

    // Brand header plus the gap below it.
    private static float HeaderBlockHeight => 102f * Scale;

    /// <summary>
    /// Card heights taken from the live font and style metrics instead of fixed pixels, so every
    /// card keeps its controls visible at 100-200 % interface scale. The same numbers size the
    /// window, which keeps "fits its content" true by construction.
    /// </summary>
    private readonly record struct DashboardMetrics(float Top, float CategoriesMinimum, float Bottom, float WindowHeight)
    {
        // Must run with the manager's visual style pushed.
        public static DashboardMetrics Measure()
        {
            var style = ImGui.GetStyle();
            var line = ImGui.GetTextLineHeightWithSpacing();
            var frame = ImGui.GetFrameHeightWithSpacing();
            var cardChrome = (style.WindowPadding.Y * 2f) + line + style.ItemSpacing.Y + 1f;
            // Status: two message lines, the activation checkbox and one action button.
            var top = cardChrome + (line * 2f) + (frame * 2f) + (style.ItemSpacing.Y * 2f);
            // Two button rows; the automation card holds a checkbox plus its helper text.
            var bottom = cardChrome + (frame * 2f);
            var categoryRows = (TranslationCategoryCatalog.All.Count + 1) / 2;
            var categories = cardChrome
                + line
                + (categoryRows * (ImGui.GetFrameHeight() + (style.CellPadding.Y * 2f)));
            var titleBar = ImGui.GetFontSize() + (style.FramePadding.Y * 2f);
            // Three card rows, each followed by an item gap and separated by one Spacing().
            var window = titleBar
                + (style.WindowPadding.Y * 2f)
                + HeaderBlockHeight
                + top + categories + bottom
                + (style.ItemSpacing.Y * 4f);
            return new DashboardMetrics(top, categories, bottom, window);
        }
    }

    private void DrawDashboard()
    {
        var style = ImGui.GetStyle();
        var metrics = DashboardMetrics.Measure();
        var topHeight = metrics.Top;
        var bottomHeight = metrics.Bottom;
        var categoriesMinimum = metrics.CategoriesMinimum;
        var gap = style.ItemSpacing.X;
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var primaryWidth = (contentWidth - gap) * 0.62f;

        DrawStatusCard(new Vector2(primaryWidth, topHeight));
        ImGui.SameLine();
        DrawCompatibilityCard(new Vector2(0f, topHeight));

        ImGui.Spacing();
        // The categories card absorbs the little spare height the window is allowed to have.
        var spareHeight = ImGui.GetContentRegionAvail().Y - bottomHeight - (style.ItemSpacing.Y * 2f);
        DrawCategoriesCard(new Vector2(0f, MathF.Floor(MathF.Max(categoriesMinimum, spareHeight))));

        ImGui.Spacing();
        DrawAutomationCard(new Vector2(primaryWidth, bottomHeight));
        ImGui.SameLine();
        DrawInformationCard(new Vector2(0f, bottomHeight));
    }

    private void DrawPenumbraDependencyScreen()
    {
        if (!ImGui.BeginChild("Dependencia Penumbra", new Vector2(0f, 0f), true))
        {
            ImGui.EndChild();
            return;
        }

        if (!_session.IsPenumbraInstalled)
        {
            DrawCardHeading("REQUISITO OBLIGATORIO", "Penumbra es necesario");
            ImGui.TextWrapped(
                "Este gestor instala y administra la traducción mediante Penumbra. Añade su repositorio oficial y luego instálalo desde el instalador de Dalamud.");

            ImGui.Spacing();
            ImGui.TextColored(Cyan, "1 · Añade el repositorio oficial de Penumbra");
            ImGui.TextWrapped(PenumbraRepositoryUrl);
            if (ImGui.Button("Copiar enlace del repositorio"))
            {
                ImGui.SetClipboardText(PenumbraRepositoryUrl);
            }

            ImGui.SameLine();
            if (ImGui.Button("Abrir ajustes de Dalamud"))
            {
                PluginInterface.OpenDalamudSettingsTo(SettingsOpenKind.Experimental);
            }

            ImGui.Spacing();
            ImGui.TextColored(Cyan, "2 · Instala Penumbra");
            ImGui.TextWrapped(
                "Después de añadir el repositorio, busca Penumbra en el instalador de plugins y selecciona Instalar.");
            if (ImGui.Button("Buscar Penumbra en el instalador"))
            {
                PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, TranslationManagerSession.PenumbraInternalName);
            }

            ImGui.SameLine();
            if (ImGui.Button("Comprobar estado de Penumbra"))
            {
                _session.RecheckPenumbra();
            }
        }
        else
        {
            DrawCardHeading("REQUISITO OBLIGATORIO", "Penumbra no está cargado");
            ImGui.TextWrapped(_session.IsPenumbraOutdated
                ? "Dalamud detecta Penumbra instalado, pero su versión no es compatible con la API actual. Actualízalo desde el instalador de plugins."
                : "Penumbra está instalado, pero no aparece cargado en esta sesión. No podemos activarlo directamente desde este gestor; puedes abrir sus controles en Dalamud y habilitarlo allí.");
            ImGui.Spacing();
            if (ImGui.Button("Abrir Penumbra en plugins instalados"))
            {
                PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.InstalledPlugins, TranslationManagerSession.PenumbraInternalName);
            }

            ImGui.SameLine();
            if (ImGui.Button("Volver a comprobar"))
            {
                _session.RecheckPenumbra();
            }

            ImGui.TextDisabled("Al cargar Penumbra, el gestor actualizará esta pantalla automáticamente.");
        }

        ImGui.EndChild();
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
        var ui = _session.UiSnapshot();
        if (ImGui.BeginChild("Estado de la traducción", size, true, DashboardCardFlags))
        {
            DrawCardHeading("GESTIÓN", "Estado de la traducción");
            if (!_session.StartupWait.IsReady)
            {
                DrawStartupWaitRequirement();
            }
            else
            {
                if (ui.CanCancelRegeneration)
                {
                    // Live pipeline step: a multi-second build must never look frozen.
                    ImGui.TextColored(Cyan, "Generando e instalando la traducción…");
                    ImGui.TextDisabled(_session.GenerationProgress ?? _session.State.Message);
                }
                else if (!_session.PenumbraAvailability.IsReady)
                {
                    DrawWrapped(
                        _session.PenumbraAvailability.Status is PenumbraAvailabilityStatus.Faulted
                            or PenumbraAvailabilityStatus.IncompatibleBreakingVersion
                            or PenumbraAvailabilityStatus.InsufficientFeatureVersion
                            or PenumbraAvailabilityStatus.InvalidModDirectory
                            ? Red
                            : Gold,
                        _session.PenumbraAvailability.UserMessage);
                    if (_session.PenumbraAvailability.Status is PenumbraAvailabilityStatus.Disabled
                        or PenumbraAvailabilityStatus.IncompatibleBreakingVersion
                        or PenumbraAvailabilityStatus.InsufficientFeatureVersion)
                    {
                        if (ImGui.SmallButton("Abrir Penumbra en plugins instalados"))
                        {
                            PluginInterface.OpenPluginInstallerTo(
                                PluginInstallerOpenKind.InstalledPlugins,
                                TranslationManagerSession.PenumbraInternalName);
                        }
                    }
                    else if (ImGui.SmallButton("Reintentar conexión con Penumbra"))
                    {
                        _session.RetryPenumbraNow();
                    }
                }
                else
                {
                    // Outcome of the last operation, missing installation, pending restart or the
                    // plain regeneration summary: the model picks the one that matters most.
                    DrawWrapped(
                        ui.StatusTone switch
                        {
                            StatusTone.Error => Red,
                            StatusTone.Warning => Gold,
                            _ => Text,
                        },
                        ui.StatusMessage);
                }

                var categories = _session.Categories;
                if (_session.ManagedModDirectory is not null && categories.ModEnabled is not null)
                {
                    ImGui.Spacing();
                    var modEnabled = _session.Configuration?.ManagedModEnabled ?? categories.ModEnabled.Value;
                    var stateUpdateRunning = _session.IsModStateUpdateRunning;
                    ImGui.BeginDisabled(
                        stateUpdateRunning
                        || ui.CanCancelRegeneration
                        || (modEnabled == false && !categories.SessionActivationAllowed));
                    if (DrawFramedCheckbox("Traducción activa en Base/Default", ref modEnabled))
                    {
                        _session.StartModStateUpdate(modEnabled);
                    }
                    ImGui.EndDisabled();
                    if (stateUpdateRunning)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled("Sincronizando…");
                    }
                }

                // The spacer is emitted only with a button, so an idle card has no phantom overflow.
                if (ui.CanCancelRegeneration)
                {
                    ImGui.Spacing();
                    if (ImGui.Button("Cancelar"))
                    {
                        _session.CancelActiveOperation();
                    }
                }
                else if (ui.CanStartRegeneration || ui.CanRestartGame)
                {
                    // Regenerating stays available while a restart is pending: after a cancelled
                    // or failed operation it is the only way to finish the job.
                    ImGui.Spacing();
                    if (ui.CanStartRegeneration && ImGui.Button(ui.PrimaryActionLabel))
                    {
                        _session.StartRegeneration(requestedByUser: true);
                    }

                    if (ui.CanRestartGame)
                    {
                        if (ui.CanStartRegeneration)
                        {
                            ImGui.SameLine();
                        }

                        DrawRestartGameButton();
                    }
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawStartupWaitRequirement()
    {
        ImGui.TextWrapped(_session.StartupWait.Message);
        var ui = _session.UiSnapshot();
        if (ui.CanCancelRegeneration)
        {
            ImGui.TextDisabled("La operación en curso puede terminar con seguridad o cancelarse.");
            if (ImGui.Button("Cancelar operación"))
            {
                _session.CancelActiveOperation();
            }
            return;
        }

        if (_session.StartupWait.CanEnable && ImGui.Button("Activar carga temprana"))
        {
            _openStartupWaitConfirmationRequested = true;
        }
        else if (_session.StartupWait.Status == DalamudStartupWaitStatus.RestartRequired)
        {
            DrawRestartGameButton();
        }
    }

    private void DrawRestartGameButton()
    {
        if (ImGui.Button("Reiniciar juego"))
        {
            _openGameRestartConfirmationRequested = true;
        }
    }

    private void DrawCompatibilityCard(Vector2 size)
    {
        var fingerprint = _session.CurrentFingerprint ?? _session.Configuration?.LastSuccessfulFingerprint;
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
                DrawWrapped(Muted, "No se ha podido comprobar la compatibilidad de FFXIV.");
            }
            else if (StringComparer.Ordinal.Equals(
                fingerprint.InstalledGameVersion,
                fingerprint.RecommendedGameVersion))
            {
                DrawWrapped(Green, "✓ Versión de FFXIV compatible.");
            }
            else
            {
                DrawWrapped(Gold, "! La versión instalada difiere de la recomendada.");
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
        var ui = _session.UiSnapshot();
        var categories = _session.Categories;
        if (ImGui.BeginChild("Categorías", size, true, DashboardCardFlags))
        {
            DrawCardHeading("CONTENIDO DEL MOD", "Categorías de traducción");
            // The message already names the collection; a "Base/Default (…) ·" prefix repeated it.
            DrawWrapped(Muted, categories.Message);
            if (!_session.StartupWait.IsReady)
            {
                ImGui.Spacing();
                ImGui.TextDisabled(_session.StartupWait.Status switch
                {
                    DalamudStartupWaitStatus.Disabled => "Activa la carga temprana para editar las categorías.",
                    DalamudStartupWaitStatus.RestartRequired => "Las categorías estarán disponibles después de reiniciar.",
                    _ => "Las categorías no están disponibles hasta verificar la carga temprana.",
                });
            }
            else if (!ui.CanEditCategories || String.IsNullOrWhiteSpace(_session.ManagedModDirectory))
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

    private void DrawAutomationCard(Vector2 size)
    {
        if (ImGui.BeginChild("Automatización", size, true, DashboardCardFlags))
        {
            DrawCardHeading("AJUSTES", "Automatización");
            if (_session.IsConfigurationReadOnly && _session.Configuration is { } futureConfiguration)
            {
                ImGui.TextColored(Gold,
                    $"Configuración v{futureConfiguration.Version}: se usarán los ajustes conocidos, pero los ajustes del plugin no se guardarán. Actualízalo para volver a editarlos.");
                ImGui.Spacing();
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
            if (ImGui.Button("Hitos de traducción", new Vector2(buttonWidth, 0f)))
            {
                _openMilestonesRequested = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Historial de sesión", new Vector2(buttonWidth, 0f)))
            {
                _openHistoryRequested = true;
            }

            if (ImGui.Button("Aviso legal", new Vector2(buttonWidth, 0f)))
            {
                _openLegalRequested = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Copiar diagnóstico", new Vector2(buttonWidth, 0f)))
            {
                ImGui.SetClipboardText(_session.UiSnapshot().Diagnostic);
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
