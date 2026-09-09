using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.App.Services;

/// <summary>Curated presentation metadata for one advanced-panel category, keyed by the pipeline's
/// stable Penumbra option domain. The detailed tooltip remains UI-owned; names and order are shared
/// with the package writer.</summary>
public sealed record CategoryInfo(string Domain, string Label, string Tooltip);

/// <summary>
/// The UI catalog enriches the package catalog with longer tooltips. Every emitted option is visible
/// here, so defaults in the patcher and checkboxes in Penumbra always refer to the same ten domains.
/// </summary>
public static class CategoryCatalog
{
    public static IReadOnlyList<CategoryInfo> All { get; } =
    [
        new(TranslationCategoryCatalog.All[0].Domain, TranslationCategoryCatalog.All[0].DisplayName,
            "Texto de misiones: títulos, objetivos y diálogos de quest/NPC, FATEs y misiones de localización (Quest, DefaultTalk, CustomTalk, NpcYell, Journal*, ContentFinderCondition, Fate, Leve)."),
        new(TranslationCategoryCatalog.All[1].Domain, TranslationCategoryCatalog.All[1].DisplayName,
            "Nombres propios y términos del mundo: NPC, criaturas, lugares, títulos, clima, razas y emotes (ENpcResident, BNpcName, PlaceName, EObjName, Title, Weather…)."),
        new(TranslationCategoryCatalog.All[2].Domain, TranslationCategoryCatalog.All[2].DisplayName,
            "Nombres de clases y jobs y sus categorías (ClassJob, ClassJobCategory). Desmárcala para dejar los nombres de clase/job en inglés."),
        new(TranslationCategoryCatalog.All[3].Domain, TranslationCategoryCatalog.All[3].DisplayName,
            "Nombres y descripciones de objetos del inventario, incl. categorías de UI, pesca/recolección y mazmorras profundas (Item, ItemUICategory, FishParameter, DeepDungeonItem…)."),
        new(TranslationCategoryCatalog.All[4].Domain, TranslationCategoryCatalog.All[4].DisplayName,
            "Objetos clave y de misión con su texto de ayuda, distintos del inventario normal (EventItem, EventItemHelp)."),
        new(TranslationCategoryCatalog.All[5].Domain, TranslationCategoryCatalog.All[5].DisplayName,
            "Monturas, acompañantes, mascotas, adornos, rollos de Orchestrion y cartas de Triple Triad (Mount, Companion, Pet, Ornament, Orchestrion, TripleTriadCard…)."),
        new(TranslationCategoryCatalog.All[6].Domain, TranslationCategoryCatalog.All[6].DisplayName,
            "Acciones de combate y artesanía, rasgos y estados (Action, ActionTransient, Trait, Status, CraftAction…)."),
        new(TranslationCategoryCatalog.All[7].Domain, TranslationCategoryCatalog.All[7].DisplayName,
            "Nombres y descripciones de logros y sus categorías (Achievement, AchievementCategory)."),
        new(TranslationCategoryCatalog.All[8].Domain, TranslationCategoryCatalog.All[8].DisplayName,
            "Mensajes del registro/log de combate y del sistema, y sus filtros (LogMessage, LogFilter, LogKind)."),
        new(TranslationCategoryCatalog.All[9].Domain, TranslationCategoryCatalog.All[9].DisplayName,
            "Texto de la interfaz: menús, comandos, ayuda/tutoriales y mensajes del sistema (Addon, Lobby, TextCommand, HowTo, Error…)."),
    ];
}
