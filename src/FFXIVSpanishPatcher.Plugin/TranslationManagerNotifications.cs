using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Textures;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Dalamud notifications raised while the window may be closed. Clicking one opens the window.
/// </summary>
internal sealed class TranslationManagerNotifications(DalamudServices dalamud, Action openWindow)
{
    private IActiveNotification? _translationRestart;
    private IActiveNotification? _pluginUpdate;

    public void DismissTranslationRestart()
    {
        _translationRestart?.DismissNow();
        _translationRestart = null;
    }

    public void DismissPluginUpdate()
    {
        _pluginUpdate?.DismissNow();
        _pluginUpdate = null;
    }

    public void ShowStartupWait(DalamudStartupWaitSnapshot snapshot)
    {
        var (title, type) = snapshot.Status switch
        {
            DalamudStartupWaitStatus.Disabled => ("Carga temprana desactivada", NotificationType.Warning),
            DalamudStartupWaitStatus.RestartRequired => ("Reinicio necesario", NotificationType.Warning),
            _ => ("No se pudo verificar Dalamud", NotificationType.Error),
        };
        Show(title, snapshot.Message, type, TimeSpan.FromSeconds(12));
    }

    public void ShowTranslationRestart()
    {
        _translationRestart?.DismissNow();
        _translationRestart = Show(
            "Reinicio necesario",
            "La traducción ya está preparada en Penumbra. Reinicia el juego para cargar sus textos desde el inicio.",
            NotificationType.Warning,
            TimeSpan.FromSeconds(12));
    }

    /// <summary>
    /// Raised once per load, at startup. It stays up longer than the others because it appears
    /// while the game is still showing its opening screens.
    /// </summary>
    public void ShowPluginUpdate(Version installed, Version published)
    {
        _pluginUpdate?.DismissNow();
        _pluginUpdate = Show(
            "Nueva versión disponible",
            $"Hay una versión nueva del gestor de traducción: {PluginUpdateCheck.Display(published)} "
            + $"(tienes la {PluginUpdateCheck.Display(installed)}). Actualízala desde /xlplugins.",
            NotificationType.Info,
            TimeSpan.FromSeconds(20));
    }

    private IActiveNotification Show(string title, string content, NotificationType type, TimeSpan duration)
    {
        var notification = dalamud.NotificationManager.AddNotification(new Notification
        {
            Title = title,
            Content = content,
            Type = type,
            Icon = new CrystalNotificationIcon(dalamud.TextureProvider.GetFromManifestResource(
                typeof(Plugin).Assembly,
                "FFXIVSpanishTranslationManager.logo-git.png")),
            InitialDuration = duration,
        });
        notification.Click += _ =>
        {
            openWindow();
            notification.DismissNow();
        };
        return notification;
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
}
