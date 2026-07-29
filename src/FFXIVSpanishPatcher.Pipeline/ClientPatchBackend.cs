using XivSpanish.GameData;
using XivSpanish.Packager;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Default backend: opens the installed FFXIV client (or a vanilla snapshot when
/// <see cref="PatchRequest.BaseExdDir"/> is set) via Lumina. This is the production path; tests use
/// a synthetic backend instead so they never need a game install.
/// </summary>
public sealed class ClientPatchBackendFactory : IPatchBackendFactory
{
    public IPatchBackend Open(PatchRequest request)
    {
        // GameLocator throws DirectoryNotFoundException when the path cannot be resolved; the
        // pipeline translates that into a GameDataError result.
        var gameData = GameLocator.Open(request.GamePath);
        var resolver = new ExdResolver(gameData);

        IBaseExdSource baseSource = request.BaseExdDir is { Length: > 0 } dir
            ? new DirectoryExdSource(dir)
            : new ClientExdSource(resolver);

        return new ClientPatchBackend(gameData, resolver, baseSource);
    }
}

internal sealed class ClientPatchBackend(object gameData, ExdResolver resolver, IBaseExdSource baseSource)
    : IPatchBackend
{
    private readonly object _gameData = gameData;
    private readonly ExdResolver _resolver = resolver;

    public IBaseExdSource BaseSource { get; } = baseSource;

    public ExdResolution ResolveExd(TranslationSourceKey key)
    {
        if (!string.IsNullOrWhiteSpace(key.ExdPath))
        {
            return ExdResolution.Resolved(key.ExdPath);
        }

        if (!key.RowId.HasValue)
        {
            return ExdResolution.UnresolvedRow();
        }

        if (_resolver.ReadHeader(key.Sheet) is null)
        {
            return ExdResolution.MissingSheet();
        }

        var location = _resolver.Resolve(key.Sheet, key.RowId.Value, "en");
        return location is null
            ? ExdResolution.UnresolvedRow()
            : ExdResolution.Resolved(location.GamePath);
    }

    public void Dispose() => (_gameData as IDisposable)?.Dispose();
}
