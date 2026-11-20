using XivSpanish.GameData;
using XivSpanish.Packager;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

public enum ExdResolutionKind
{
    Resolved,
    MissingSheet,
    UnresolvedRow,
}

public readonly record struct ExdResolution(ExdResolutionKind Kind, string? Path = null)
{
    public static ExdResolution Resolved(string path) => new(ExdResolutionKind.Resolved, path);
    public static ExdResolution MissingSheet() => new(ExdResolutionKind.MissingSheet);
    public static ExdResolution UnresolvedRow() => new(ExdResolutionKind.UnresolvedRow);

    /// <summary>
    /// The one rule that maps a source key to its EXD page over a live Lumina resolver: the key's
    /// own <c>ExdPath</c> when it has one, otherwise the page that holds the row. The desktop app
    /// and the Dalamud plugin read the game through different backends but must agree on this.
    /// </summary>
    public static ExdResolution For(ExdResolver resolver, TranslationSourceKey key)
    {
        if (!string.IsNullOrWhiteSpace(key.ExdPath))
        {
            return Resolved(key.ExdPath);
        }

        if (!key.RowId.HasValue)
        {
            return UnresolvedRow();
        }

        if (resolver.ReadHeader(key.Sheet) is null)
        {
            return MissingSheet();
        }

        var location = resolver.Resolve(key.Sheet, key.RowId.Value, "en");
        return location is null ? UnresolvedRow() : Resolved(location.GamePath);
    }
}

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
    ExdResolution ResolveExd(TranslationSourceKey key);
}

/// <summary>Opens an <see cref="IPatchBackend"/> for a request. The pipeline owns the lifetime
/// (disposes it after the run).</summary>
public interface IPatchBackendFactory
{
    IPatchBackend Open(PatchRequest request);
}
