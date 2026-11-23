using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Dalamud entry point. It receives Dalamud's services, builds the manager's parts and connects
/// them to Dalamud's window and command hooks; the work lives in
/// <see cref="TranslationManagerSession"/> and the drawing in <see cref="TranslationManagerWindow"/>.
/// </summary>
public sealed class Plugin : IAsyncDalamudPlugin
{
    private const string OpenCommandName = "/ffxives";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private readonly TranslationManagerSession _session;
    private readonly TranslationManagerWindow _window;

    public Plugin()
    {
        var dalamud = new DalamudServices(
            PluginInterface,
            DataManager,
            Log,
            CommandManager,
            Framework,
            TextureProvider,
            NotificationManager);
        // A notification can be clicked only after the window exists; until then it has nothing to open.
        TranslationManagerWindow? window = null;
        var notifications = new TranslationManagerNotifications(dalamud, () => window?.Open());
        _session = new TranslationManagerSession(dalamud, notifications);
        _window = window = new TranslationManagerWindow(dalamud, _session);

        CommandManager.AddHandler(OpenCommandName, new CommandInfo((_, _) => _window.Open())
        {
            HelpMessage = "Abre FFXIV Spanish Translation Manager.",
        });
        PluginInterface.UiBuilder.Draw += _window.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += _window.Open;
        PluginInterface.UiBuilder.OpenMainUi += _window.Open;
    }

    /// <summary>Checks that Dalamud-owned game data is readable without taking ownership of it.</summary>
    public Task LoadAsync(CancellationToken cancellationToken) => _session.LoadAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        PluginInterface.UiBuilder.Draw -= _window.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= _window.Open;
        PluginInterface.UiBuilder.OpenMainUi -= _window.Open;
        CommandManager.RemoveHandler(OpenCommandName);
        _window.Dispose();
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
