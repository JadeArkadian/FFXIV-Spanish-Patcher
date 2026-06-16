namespace XivSpanish.Translation;

/// <summary>
/// Maps a translation entry to its coverage domain. Domains and their sheets come from the
/// Sprint 2 structure analysis (<c>docs/sprint/analysis/SPRINT-002-STRUCTURE-ANALYSIS.md</c>,
/// section T-B2): the domain of an entry is decided by its <c>sourceKey.sheet</c>.
/// </summary>
/// <remarks>
/// The default mapping is the T-B2 domain table. Any sheet not listed there is grouped under
/// its own sheet name, so coverage is still reported (never silently dropped) and a new sheet
/// shows up as its own domain until it is folded into a curated one. The map is configurable:
/// callers can pass their own sheet to domain pairs.
/// </remarks>
public sealed class DomainMap
{
    /// <summary>Domain assigned when an entry has no usable <c>sourceKey.sheet</c>.</summary>
    public const string UnknownDomain = "(unknown)";

    private readonly IReadOnlyDictionary<string, string> sheetToDomain;

    public DomainMap(IReadOnlyDictionary<string, string> sheetToDomain)
    {
        ArgumentNullException.ThrowIfNull(sheetToDomain);
        this.sheetToDomain = new Dictionary<string, string>(sheetToDomain, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The Sprint 2 (T-B2) domain to sheets mapping. Keys are sheet names, values the domain
    /// label. Sheets absent here fall back to their own name as the domain.
    /// </summary>
    public static DomainMap Sprint2Default { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Interfaz
        ["Addon"] = "interfaz",
        ["AddonTransient"] = "interfaz",
        ["Completion"] = "interfaz",
        ["Lobby"] = "interfaz",
        // Nombres
        ["ENpcResident"] = "nombres",
        ["BNpcName"] = "nombres",
        ["PlaceName"] = "nombres",
        ["CutsceneName"] = "nombres",
        ["EObjName"] = "nombres",
        // Items
        ["Item"] = "items",
        // Acciones / habilidades
        ["Action"] = "acciones",
        ["Trait"] = "acciones",
        ["Status"] = "acciones",
        // Misiones
        ["Quest"] = "misiones",
        ["DefaultTalk"] = "misiones",
        ["CustomTalk"] = "misiones",
    });

    /// <summary>Resolves the domain for one entry from its <c>sourceKey.sheet</c>.</summary>
    public string Resolve(TranslationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var sheet = entry.SourceKey?.Sheet;
        if (string.IsNullOrWhiteSpace(sheet))
        {
            return UnknownDomain;
        }

        return sheetToDomain.TryGetValue(sheet, out var domain) ? domain : sheet;
    }
}
