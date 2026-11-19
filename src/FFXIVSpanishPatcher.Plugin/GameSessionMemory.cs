namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Facts about the running game process that must outlive one plugin instance. Disabling and
/// re-enabling the plugin unloads its assembly, but the game keeps every file it already loaded:
/// what was true for the session before the reload is still true after it.
/// </summary>
public interface IGameSessionMemory
{
    string? Get(string key);

    void Set(string key, string value);
}

/// <summary>
/// Keeps plain strings in the process-wide <see cref="AppContext"/>. They end with the game
/// process, so nothing can leak into the next session, and being framework types they never pin
/// the plugin's unloadable assembly.
/// </summary>
internal sealed class ProcessGameSessionMemory : IGameSessionMemory
{
    private const string Prefix = "FFXIVSpanishTranslationManager.";

    public string? Get(string key) => AppContext.GetData(Prefix + key) as string;

    public void Set(string key, string value) => AppContext.SetData(Prefix + key, value);
}
