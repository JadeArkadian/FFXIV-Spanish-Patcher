using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Resolves a translation entry to its category domain and decides whether a given selection
/// includes it. The domain mapping is the curated <see cref="DomainMap.Sprint2Default"/>
/// (sheet -> domain: interfaz / nombres / items / acciones / misiones); a sheet not listed there
/// falls back to its own name so nothing is silently dropped. The GUI maps these domains to the
/// pretty labels and tooltips of the advanced panel (F6).
/// </summary>
public static class TranslationCategories
{
    /// <summary>The curated sheet -> domain map shared with the upstream coverage tooling.</summary>
    public static DomainMap Domains => DomainMap.Sprint2Default;

    /// <summary>Category domain of an entry, e.g. <c>items</c> or <c>misiones</c>.</summary>
    public static string DomainOf(TranslationEntry entry) => Domains.Resolve(entry);

    /// <summary>
    /// True when <paramref name="selected"/> is null (all categories) or contains the entry's
    /// domain (case-insensitive).
    /// </summary>
    public static bool IsSelected(TranslationEntry entry, IReadOnlySet<string>? selected)
        => selected is null || selected.Contains(DomainOf(entry));

    /// <summary>Builds a case-insensitive selection set, or null when no filter is requested.</summary>
    public static IReadOnlySet<string>? BuildSelection(IReadOnlyCollection<string>? categories)
        => categories is null ? null : new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
}
