namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// The address of the translation report form, with the context a report needs already filled in.
/// The form reads the query as hidden fields; nothing is sent until the user submits it.
/// </summary>
public static class TranslationReportLink
{
    public const string FormUrl = "https://tally.so/r/1ARKzp";
    public const string Subcommand = "reportar";

    public static bool IsReportCommand(string? arguments)
        => string.Equals(arguments?.Trim(), Subcommand, StringComparison.OrdinalIgnoreCase);

    public static string Build(Version? pluginVersion, string? gameVersion, uint territoryId)
    {
        var plugin = pluginVersion is null ? string.Empty : PluginUpdateCheck.Display(pluginVersion);
        return $"{FormUrl}?version={Uri.EscapeDataString(plugin)}"
               + $"&juego={Uri.EscapeDataString(gameVersion ?? string.Empty)}"
               + $"&zona={territoryId}";
    }
}
