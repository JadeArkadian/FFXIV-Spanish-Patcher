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

    public void DismissTranslationRestart()
    {
        _translationRestart?.DismissNow();
        _translationRestart = null;
    }

    public void ShowStartupWait(DalamudStartupWaitSnapshot snapshot)
    {
        var (title, type) = snapshot.Status switch
        {
            DalamudStartupWaitStatus.Disabled => ("Carga temprana desactivada", NotificationType.Warning),
            DalamudStartupWaitStatus.RestartRequired => ("Reinicio necesario", NotificationType.Warning),
            _ => ("No se pudo verificar Dalamud", NotificationType.Error),
        };
        var notification = dalamud.NotificationManager.AddNotification(new Notification
        {
            Title = title,
            Content = snapshot.Message,
            Type = type,
            Icon = new CrystalNotificationIcon(dalamud.TextureProvider.GetFromManifestResource(
                typeof(Plugin).Assembly,
                "FFXIVSpanishTranslationManager.logo-git.png")),
            InitialDuration = TimeSpan.FromSeconds(12),
        });
        notification.Click += _ =>
        {
            openWindow();
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

    public void ShowTranslationRestart()
    {
        _translationRestart?.DismissNow();
        var notification = dalamud.NotificationManager.AddNotification(new Notification
        {
            Title = "Reinicio necesario",
            Content = "La traducción ya está preparada en Penumbra. Reinicia el juego para cargar sus textos desde el inicio.",
            Type = NotificationType.Warning,
            Icon = new CrystalNotificationIcon(dalamud.TextureProvider.GetFromManifestResource(
                typeof(Plugin).Assembly,
                "FFXIVSpanishTranslationManager.logo-git.png")),
            InitialDuration = TimeSpan.FromSeconds(12),
        });
        notification.Click += _ =>
        {
            openWindow();
            notification.DismissNow();
        };
        _translationRestart = notification;
    }
}
