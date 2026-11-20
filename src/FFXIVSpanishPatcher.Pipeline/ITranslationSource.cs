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

/// <summary>Wraps an already-materialized list of entries. Primary use is tests and the embedded
/// source (which decompresses to a list).</summary>
public sealed class ListTranslationSource(IReadOnlyList<TranslationEntry> entries) : ITranslationSource
{
    private readonly IReadOnlyList<TranslationEntry> _entries = entries;

    public IReadOnlyList<TranslationEntry> Load() => _entries;
}
