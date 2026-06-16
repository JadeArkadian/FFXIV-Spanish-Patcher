using Lumina.Data;

namespace XivSpanish.GameData;

/// <summary>
/// Maps between user-facing language codes, Lumina <see cref="Language"/> values,
/// and the suffix used in EXD page file names (e.g. <c>addon_0_en.exd</c>).
/// </summary>
public static class LanguageCodes
{
    public static Language ToLumina(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "en" or "english" => Language.English,
        "ja" or "jp" or "japanese" => Language.Japanese,
        "de" or "german" => Language.German,
        "fr" or "french" => Language.French,
        "" or "none" => Language.None,
        _ => Language.English,
    };

    /// <summary>
    /// EXD file-name suffix for a language. <see cref="Language.None"/> yields an empty
    /// suffix, producing language-neutral page names like <c>gilshop_0.exd</c>.
    /// </summary>
    public static string Suffix(Language language) => language switch
    {
        Language.Japanese => "ja",
        Language.English => "en",
        Language.German => "de",
        Language.French => "fr",
        Language.ChineseSimplified => "chs",
        Language.ChineseTraditional => "cht",
        Language.Korean => "ko",
        _ => string.Empty,
    };

    public static string Suffix(string? code) => Suffix(ToLumina(code));
}
