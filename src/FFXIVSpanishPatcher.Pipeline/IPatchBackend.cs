using XivSpanish.Packager;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// The game-data side of a pipeline run: the source of base (pre-patch) EXD bytes/layout and the
/// resolver that maps a source key to its EXD page path. Opening this is where the live FFXIV
/// client (or a vanilla snapshot) is touched, so it is abstracted behind a factory: the shipped app
/// opens the client, tests inject a synthetic backend with no game install.
/// </summary>
public interface IPatchBackend : IDisposable
{
    /// <summary>Base EXD bytes and per-sheet layout (client or snapshot).</summary>
    IBaseExdSource BaseSource { get; }

    /// <summary>
    /// Resolves the EXD page path for a source key, preferring the key's own <c>ExdPath</c> and
    /// falling back to a live resolve. Returns null when the path cannot be determined.
    /// </summary>
    string? ResolveExdPath(TranslationSourceKey key);
}

/// <summary>Opens an <see cref="IPatchBackend"/> for a request. The pipeline owns the lifetime
/// (disposes it after the run).</summary>
public interface IPatchBackendFactory
{
    IPatchBackend Open(PatchRequest request);
}
