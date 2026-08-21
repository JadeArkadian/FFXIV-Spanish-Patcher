using Dalamud.Plugin.Services;
using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.GameData;
using XivSpanish.Packager;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Pipeline backend over Dalamud-owned Lumina data. It deliberately never disposes GameData: the
/// service lifetime belongs to Dalamud, while the pipeline only owns this lightweight adapter.
/// </summary>
internal sealed class DalamudPatchBackendFactory(IDataManager dataManager) : IPatchBackendFactory
{
    public IPatchBackend Open(PatchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.BaseExdDir))
        {
            throw new InvalidOperationException("El plugin no admite snapshots EXD externos.");
        }

        var resolver = new ExdResolver(dataManager.GameData);
        return new DalamudPatchBackend(resolver, new ClientExdSource(resolver));
    }
}

internal sealed class DalamudPatchBackend(ExdResolver resolver, IBaseExdSource baseSource) : IPatchBackend
{
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

        if (resolver.ReadHeader(key.Sheet) is null)
        {
            return ExdResolution.MissingSheet();
        }

        var location = resolver.Resolve(key.Sheet, key.RowId.Value, "en");
        return location is null ? ExdResolution.UnresolvedRow() : ExdResolution.Resolved(location.GamePath);
    }

    public void Dispose()
    {
        // IDataManager.GameData is owned by Dalamud.
    }
}
