using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>Stable Penumbra option metadata shared by package generation and the desktop UI.</summary>
public sealed record TranslationCategoryDefinition(string Domain, string DisplayName, string Description, int Order);

/// <summary>
/// The ten stable options emitted into every categorized Penumbra package. Their domain and order
/// are persistence contracts: changing either requires an explicit settings migration.
/// </summary>
public static class TranslationCategoryCatalog
{
    public const string GroupName = "Categorías de traducción";

    public static IReadOnlyList<TranslationCategoryDefinition> All { get; } =
    [
        new("misiones", "Misiones", "Texto de misiones, objetivos, diálogos y contenido.", 0),
        new("nombres", "Nombres y lugares", "Nombres propios, criaturas, lugares y términos del mundo.", 1),
        new("clases", "Clases y jobs", "Nombres de clases, jobs y sus categorías.", 2),
        new("items", "Objetos", "Nombres y descripciones de objetos, inventario y recolección.", 3),
        new("eventos", "Objetos de evento", "Objetos clave y de misión, con su texto de ayuda.", 4),
        new("coleccionables", "Coleccionables", "Monturas, acompañantes, adornos, rollos y cartas.", 5),
        new("acciones", "Acciones", "Acciones, rasgos, estados y habilidades de artesanía.", 6),
        new("logros", "Logros", "Logros, descripciones y categorías relacionadas.", 7),
        new("registro", "Registro", "Mensajes y filtros del registro de combate y sistema.", 8),
        new("interfaz", "Interfaz", "Menús, comandos, ayuda, tutoriales y mensajes del sistema.", 9),
    ];

    private static readonly IReadOnlyDictionary<string, TranslationCategoryDefinition> ByDomain =
        All.ToDictionary(category => category.Domain, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string domain, out TranslationCategoryDefinition definition)
        => ByDomain.TryGetValue(domain, out definition!);

    /// <summary>
    /// Resolves an entry to one of the ten emitted options. Unmapped sheets remain packageable and
    /// fall into Interfaz instead of disappearing into an unselectable private category.
    /// </summary>
    public static string DomainOf(TranslationEntry entry)
    {
        var domain = TranslationCategories.DomainOf(entry);
        return TryGet(domain, out var definition) ? definition.Domain : "interfaz";
    }

    /// <summary>Builds the Multi-group bitfield. A null selection means all categories enabled.</summary>
    public static int BuildDefaultSettings(IReadOnlyCollection<string>? selectedDomains)
    {
        var selected = selectedDomains is null
            ? null
            : new HashSet<string>(selectedDomains, StringComparer.OrdinalIgnoreCase);
        var settings = 0;
        foreach (var category in All)
        {
            if (selected is null || selected.Contains(category.Domain))
            {
                settings |= 1 << category.Order;
            }
        }

        return settings;
    }
}
