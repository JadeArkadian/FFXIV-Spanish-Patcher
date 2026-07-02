using System.Text.Json;

namespace XivSpanish.Translation;

/// <summary>
/// Single canonical serializer for the JSONL translation corpus. The on-disk byte format
/// (one <see cref="TranslationEntry"/> per line) is REAL committed data; these options lock
/// that format and must not change. <see cref="TranslationEntry"/> and
/// <see cref="TranslationSourceKey"/> carry explicit <c>[JsonPropertyName]</c> attributes, so
/// no naming policy is needed to produce camelCase keys.
/// </summary>
public static class JsonlSerialization
{
    /// <summary>Serialize one entry to its canonical single-line JSON form (no trailing newline).</summary>
    public static string Serialize(TranslationEntry entry)
        => JsonSerializer.Serialize(entry, TranslationJsonContext.JsonlWrite.TranslationEntry);

    public static TranslationEntry? Deserialize(string json)
        => JsonSerializer.Deserialize(json, TranslationJsonContext.Read.TranslationEntry);

    public static List<TranslationEntry>? DeserializeList(string json)
        => JsonSerializer.Deserialize(json, TranslationJsonContext.Read.ListTranslationEntry);
}
