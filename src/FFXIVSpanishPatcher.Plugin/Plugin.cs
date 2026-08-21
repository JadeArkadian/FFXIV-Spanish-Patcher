using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Minimal Dalamud entrypoint used to prove shared-engine compatibility.</summary>
public sealed class Plugin : IAsyncDalamudPlugin
{
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly PluginLoadProbe _loadProbe = new(new DalamudGameDataProbe(DataManager));

    /// <summary>Checks that Dalamud-owned game data is readable without taking ownership of it.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _loadProbe.LoadAsync(cancellationToken).ConfigureAwait(false);
        Log.Information("Compatibilidad con Dalamud y motor compartido verificada.");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _loadProbe.DisposeAsync();
}
