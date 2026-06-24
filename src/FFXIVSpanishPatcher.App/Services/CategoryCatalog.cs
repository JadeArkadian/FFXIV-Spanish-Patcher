namespace FFXIVSpanishPatcher.App.Services;

/// <summary>Curated presentation metadata for one advanced-panel category, keyed by the pipeline's
/// domain label. The label/tooltip/order are controlled here (hybrid model); whether the category is
/// shown and its count come from the embedded manifest at runtime.</summary>
public sealed record CategoryInfo(string Domain, string Label, string Tooltip);

/// <summary>
/// The curated category catalog. Domains match the patcher taxonomy in
/// <c>TranslationCategories.Domains</c>, the unit the pipeline filters on. A domain present in the
/// manifest but absent here would still appear (gated dynamically); listing it here just gives it a
/// nice Spanish label, tooltip and a stable order. The order below is the advanced-panel order.
/// </summary>
public static class CategoryCatalog
{
    public static IReadOnlyList<CategoryInfo> All { get; } =
    [
        new("misiones", "Misiones (quests)",
            "Texto de misiones: títulos, objetivos y diálogos de quest (Quest, DefaultTalk, CustomTalk, Journal*, ContentFinderCondition)."),
        new("nombres", "Nombres (NPC, lugares)",
            "Nombres propios y términos del mundo: NPC, criaturas, lugares, títulos, clima, razas y emotes (ENpcResident, BNpcName, PlaceName, EObjName, Title, Weather…)."),
        new("clases", "Clases y Jobs",
            "Nombres de clases y jobs y sus categorías (ClassJob, ClassJobCategory). Desmárcala para dejar los nombres de clase/job en inglés."),
        new("items", "Objetos (items)",
            "Nombres y descripciones de objetos del inventario, incl. categorías de UI, pesca/recolección y mazmorras profundas (Item, ItemUICategory, FishParameter, DeepDungeonItem…)."),
        new("eventos", "Objetos de evento",
            "Objetos clave y de misión con su texto de ayuda, distintos del inventario normal (EventItem, EventItemHelp)."),
        new("coleccionables", "Coleccionables",
            "Monturas, acompañantes, mascotas, adornos, rollos de Orchestrion y cartas de Triple Triad (Mount, Companion, Pet, Ornament, Orchestrion, TripleTriadCard…)."),
        new("acciones", "Acciones y habilidades",
            "Acciones de combate y artesanía, rasgos y estados (Action, ActionTransient, Trait, Status, CraftAction…)."),
        new("logros", "Logros",
            "Nombres y descripciones de logros y sus categorías (Achievement, AchievementCategory)."),
        new("registro", "Registro de combate",
            "Mensajes del registro/log de combate y del sistema, y sus filtros (LogMessage, LogFilter, LogKind)."),
        new("interfaz", "Interfaz (UI)",
            "Texto de la interfaz: menús, comandos, ayuda/tutoriales y mensajes del sistema (Addon, Lobby, TextCommand, HowTo, Error…)."),
    ];
}
