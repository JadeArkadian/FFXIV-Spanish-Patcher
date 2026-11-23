using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// The Dalamud services the manager uses, handed to each part explicitly. Only the plugin entry
/// point touches Dalamud's static injection; everything else receives this record.
/// </summary>
internal sealed record DalamudServices(
    IDalamudPluginInterface PluginInterface,
    IDataManager DataManager,
    IPluginLog Log,
    ICommandManager CommandManager,
    IFramework Framework,
    ITextureProvider TextureProvider,
    INotificationManager NotificationManager);
