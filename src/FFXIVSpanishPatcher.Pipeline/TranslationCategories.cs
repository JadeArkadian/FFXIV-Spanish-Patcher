using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Resolves a translation entry to its category domain and decides whether a given selection
/// includes it. The domain map is the patcher's own curated taxonomy (<see cref="PatcherDomains"/>),
/// a superset of the upstream <see cref="DomainMap.Sprint2Default"/>: it adds the sheets that became
/// translated after Sprint 2 so every shipped sheet lands in a visible, toggleable advanced-panel
/// category instead of an invisible per-sheet fallback bucket. A sheet still not listed falls back to
/// its own name so nothing is silently dropped. The GUI maps these domains to the pretty labels and
/// tooltips of the advanced panel (see <c>CategoryCatalog</c>).
/// </summary>
public static class TranslationCategories
{
    /// <summary>The curated sheet -> domain map used by the patcher's advanced panel.</summary>
    public static DomainMap Domains => PatcherDomains;

    /// <summary>
    /// Patcher taxonomy: every translated sheet -> one of the advanced-panel domains. Relevant,
    /// distinct sheets get their own domain (items, acciones, logros, registro, eventos,
    /// coleccionables); the long tail is folded into the broader existing buckets (interfaz,
    /// nombres, misiones). This lives here (not in vendored <see cref="DomainMap"/>) because it is a
    /// presentation decision of the patcher; the boundary rule keeps <c>vendor/</c> read-only.
    /// </summary>
    private static readonly DomainMap PatcherDomains = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Interfaz: UI chrome, commands, help/tutorials, system messages, profile/plate UI.
        ["Addon"] = "interfaz",
        ["AddonTransient"] = "interfaz",
        ["Completion"] = "interfaz",
        ["Lobby"] = "interfaz",
        ["TextCommand"] = "interfaz",
        ["MainCommand"] = "interfaz",
        ["MainCommandCategory"] = "interfaz",
        ["ExtraCommand"] = "interfaz",
        ["QuickChat"] = "interfaz",
        ["HowTo"] = "interfaz",
        ["HowToCategory"] = "interfaz",
        ["GuidePageString"] = "interfaz",
        ["ContentsTutorial"] = "interfaz",
        ["BannerDecoration"] = "interfaz",
        ["CharaCardBase"] = "interfaz",
        ["CharaCardPlayStyle"] = "interfaz",
        ["Error"] = "interfaz",
        ["MultipleHelp"] = "interfaz",
        ["MultipleHelpString"] = "interfaz",
        ["OnlineStatus"] = "interfaz",

        // Nombres: NPC/creatures, places, world flavor and character-creation lore terms.
        ["ENpcResident"] = "nombres",
        ["BNpcName"] = "nombres",
        ["PlaceName"] = "nombres",
        ["CutsceneName"] = "nombres",
        ["EObjName"] = "nombres",
        ["Title"] = "nombres",
        ["Weather"] = "nombres",
        ["Race"] = "nombres",
        ["Tribe"] = "nombres",
        ["BeastTribe"] = "nombres",
        ["GuardianDeity"] = "nombres",
        ["Town"] = "nombres",
        ["Aetheryte"] = "nombres",
        ["GrandCompany"] = "nombres",
        ["MonsterNote"] = "nombres",
        ["Emote"] = "nombres",
        ["EmoteCategory"] = "nombres",

        // Clases / trabajos: nombres de clases y trabajos y sus categorías (toggle propio para que
        // el usuario pueda dejarlos en inglés sin afectar al resto de nombres).
        ["ClassJob"] = "clases",
        ["ClassJobCategory"] = "clases",

        // Items: inventory, gear/UI categories, gathering/fishing and deep-dungeon items.
        ["Item"] = "items",
        ["ItemUICategory"] = "items",
        ["ItemSearchCategory"] = "items",
        ["ItemSeries"] = "items",
        ["ItemSpecialBonus"] = "items",
        ["DeepDungeonItem"] = "items",
        ["DeepDungeonMagicStone"] = "items",
        ["SpearfishingItem"] = "items",
        ["FishParameter"] = "items",
        ["GatheringPointName"] = "items",
        ["GatheringPointBonusType"] = "items",

        // Acciones / habilidades: combat and crafting actions, traits, statuses.
        ["Action"] = "acciones",
        ["ActionTransient"] = "acciones",
        ["ActionCategory"] = "acciones",
        ["Trait"] = "acciones",
        ["TraitTransient"] = "acciones",
        ["Status"] = "acciones",
        ["CraftAction"] = "acciones",
        ["CompanyAction"] = "acciones",
        ["PetAction"] = "acciones",
        ["GeneralAction"] = "acciones",
        ["BuddyAction"] = "acciones",

        // Misiones: quest text/dialogue, journal structure and duty names.
        ["Quest"] = "misiones",
        ["DefaultTalk"] = "misiones",
        ["CustomTalk"] = "misiones",
        ["JournalGenre"] = "misiones",
        ["JournalCategory"] = "misiones",
        ["JournalSection"] = "misiones",
        ["ContentFinderCondition"] = "misiones",
        ["ContentType"] = "misiones",

        // Logros: achievements.
        ["Achievement"] = "logros",
        ["AchievementCategory"] = "logros",

        // Registro: combat/system log messages.
        ["LogMessage"] = "registro",
        ["LogFilter"] = "registro",
        ["LogKind"] = "registro",

        // Eventos: key/event items and their help text (distinct from inventory items).
        ["EventItem"] = "eventos",
        ["EventItemHelp"] = "eventos",

        // Coleccionables: mounts, minions, pets, ornaments, orchestrion and Triple Triad.
        ["Mount"] = "coleccionables",
        ["Companion"] = "coleccionables",
        ["Pet"] = "coleccionables",
        ["Ornament"] = "coleccionables",
        ["Orchestrion"] = "coleccionables",
        ["TripleTriadCard"] = "coleccionables",
        ["TripleTriadRule"] = "coleccionables",
        ["TripleTriadCompetition"] = "coleccionables",
    });

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
