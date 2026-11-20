using Dalamud.Plugin.Services;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// The build itself runs off the game thread, but Penumbra IPC is invoked only from Dalamud's
/// framework thread. Waiting happens on the worker that owns the installation transaction.
/// </summary>
internal sealed class FrameworkPenumbraModRegistry(IPenumbraModRegistry inner, IFramework framework)
    : IPenumbraModRegistry
{
    public IReadOnlyDictionary<string, string> GetModList()
        => framework.RunOnFrameworkThread(inner.GetModList).GetAwaiter().GetResult();

    public int AddMod(string directoryName)
        => framework.RunOnFrameworkThread(() => inner.AddMod(directoryName)).GetAwaiter().GetResult();

    public int ReloadMod(string directoryName)
        => framework.RunOnFrameworkThread(() => inner.ReloadMod(directoryName)).GetAwaiter().GetResult();

    public int SetEnabled(string directoryName, bool enabled)
        => framework.RunOnFrameworkThread(() => inner.SetEnabled(directoryName, enabled)).GetAwaiter().GetResult();
}
