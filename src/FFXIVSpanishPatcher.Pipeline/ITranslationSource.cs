using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Source of the approved translation entries the pipeline packages. Abstracted so the entries can
/// come from an embedded resource (the shipped app, F2), a manifest file/dir (tooling), or an
/// in-memory list (tests), without the pipeline knowing which.
/// </summary>
public interface ITranslationSource
{
    IReadOnlyList<TranslationEntry> Load();
}

/// <summary>Loads entries from a manifest path (JSONL / JSON array / index dir) via the shared
/// <see cref="ManifestLoader"/>. Used by tooling and tests; the shipped app uses an embedded
/// source added in F2.</summary>
public sealed class ManifestFileTranslationSource(string path) : ITranslationSource
{
    private readonly string _path = path;

    public IReadOnlyList<TranslationEntry> Load() => ManifestLoader.Load(_path);
}

/// <summary>Wraps an already-materialized list of entries. Primary use is tests and the embedded
/// source (which decompresses to a list).</summary>
public sealed class ListTranslationSource(IReadOnlyList<TranslationEntry> entries) : ITranslationSource
{
    private readonly IReadOnlyList<TranslationEntry> _entries = entries;

    public IReadOnlyList<TranslationEntry> Load() => _entries;
}
