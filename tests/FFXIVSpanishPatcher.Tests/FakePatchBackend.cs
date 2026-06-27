using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.GameData;
using XivSpanish.Packager;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Tests;

/// <summary>In-memory <see cref="IBaseExdSource"/> over synthetic EXD pages and layouts: lets the
/// pipeline run with no FFXIV install. Field names are left empty so replacements fall back to
/// content matching (the fixtures use empty source-key fields).</summary>
internal sealed class FakeExdSource : IBaseExdSource
{
    private readonly Dictionary<string, byte[]> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExdLayout> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _fieldNames = new(StringComparer.OrdinalIgnoreCase);

    public FakeExdSource AddPage(string exdPath, byte[] bytes)
    {
        _pages[exdPath] = bytes;
        return this;
    }

    public FakeExdSource AddLayout(string sheet, ExdLayout layout)
    {
        _layouts[sheet] = layout;
        return this;
    }

    public FakeExdSource AddFieldNames(string sheet, params string[] fieldNames)
    {
        _fieldNames[sheet] = fieldNames;
        return this;
    }

    public byte[]? ReadBaseExd(string exdPath) => _pages.GetValueOrDefault(exdPath);

    public ExdLayout? ReadStringLayout(string sheet) => _layouts.TryGetValue(sheet, out var layout) ? layout : null;

    public IReadOnlyList<string> ResolveFieldNames(string sheet, int stringColumnCount)
        => _fieldNames.GetValueOrDefault(sheet) ?? [];
}

/// <summary>Backend over a <see cref="FakeExdSource"/>; resolves EXD paths straight from the source
/// key's own <c>ExdPath</c> (the fixtures set it), so no client is ever opened.</summary>
internal sealed class FakePatchBackend(IBaseExdSource source) : IPatchBackend
{
    public IBaseExdSource BaseSource { get; } = source;

    public string? ResolveExdPath(TranslationSourceKey key) => key.ExdPath;

    public void Dispose()
    {
    }
}

internal sealed class FakePatchBackendFactory(IBaseExdSource source) : IPatchBackendFactory
{
    public IPatchBackend Open(PatchRequest request) => new FakePatchBackend(source);
}
