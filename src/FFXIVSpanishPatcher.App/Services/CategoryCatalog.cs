namespace FFXIVSpanishPatcher.App.Services;

/// <summary>Curated presentation metadata for one advanced-panel category, keyed by the pipeline's
/// domain label. The label/tooltip/order are controlled here (hybrid model); whether the category is
/// shown and its count come from the embedded manifest at runtime.</summary>
public sealed record CategoryInfo(string Domain, string Label, string Tooltip);

/// <summary>
/// The curated category catalog. Domains match <c>TranslationCategories</c> /
/// <c>DomainMap.Sprint2Default</c>, the unit the pipeline filters on. A domain present in the
/// manifest but absent here would still appear (gated dynamically); listing it here just gives it a
/// nice Spanish label, tooltip and a stable order.
/// </summary>
public static class CategoryCatalog
{
    public static IReadOnlyList<CategoryInfo> All { get; } =
    [
        new("misiones", "Misiones (quests)",
            "Texto de misiones: títulos, objetivos y diálogos de quest (Quest, DefaultTalk, CustomTalk)."),
        new("nombres", "Nombres (NPC, lugares)",
            "Nombres propios: NPC, criaturas, lugares y objetos del mundo (ENpcResident, BNpcName, PlaceName, EObjName)."),
        new("items", "Objetos (items)",
            "Nombres y descripciones de objetos del inventario (Item)."),
        new("acciones", "Acciones y habilidades",
            "Acciones, rasgos y estados de combate (Action, Trait, Status)."),
        new("interfaz", "Interfaz (UI)",
            "Texto de la interfaz: menús, ventanas y mensajes del sistema (Addon, Completion, Lobby)."),
    ];
}
