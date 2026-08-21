using Dalamud.Plugin.Services;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Small boundary around game-data access so load and cancellation can be tested without Dalamud.</summary>
public interface IPluginGameDataProbe
{
    /// <summary>Reads known EXH and EXD resources from Dalamud-owned game data.</summary>
    bool TryReadKnownExcel(CancellationToken cancellationToken);
}

/// <summary>Runs the compatibility check outside Dalamud's framework thread and owns only its worker.</summary>
public sealed class PluginLoadProbe : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IPluginGameDataProbe _gameData;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _loadTask;
    private bool _disposed;

    public PluginLoadProbe(IPluginGameDataProbe gameData)
    {
        _gameData = gameData;
    }

    /// <summary>Starts exactly one background compatibility check.</summary>
    public Task LoadAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _loadTask ??= RunAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task? loadTask;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdown.Cancel();
            loadTask = _loadTask;
        }

        if (loadTask is not null)
        {
            try
            {
                await loadTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                // Expected during Dalamud unload.
            }
            catch when (loadTask.IsFaulted)
            {
                // LoadAsync already delivered this failure; unload must still finish cleanly.
            }
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken startupCancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            startupCancellationToken,
            _shutdown.Token);
        var cancellationToken = linkedCancellation.Token;
        cancellationToken.ThrowIfCancellationRequested();

        var readable = await Task.Run(
                () => _gameData.TryReadKnownExcel(cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!readable)
        {
            throw new InvalidOperationException("Dalamud no pudo leer exd/Addon.exh o exd/Addon_0_en.exd.");
        }
    }
}

internal sealed class DalamudGameDataProbe(IDataManager dataManager) : IPluginGameDataProbe
{
    public bool TryReadKnownExcel(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exh = dataManager.GameData.GetFile("exd/Addon.exh");
        cancellationToken.ThrowIfCancellationRequested();
        var exd = dataManager.GameData.GetFile("exd/Addon_0_en.exd");
        return exh is not null && exd is not null;
    }
}
