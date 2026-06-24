using System.Text.Json.Serialization;

namespace XivSpanish.Translation;

public sealed class TranslationSourceKey
{
    [JsonPropertyName("sheet")]
    public string Sheet { get; set; } = string.Empty;

    [JsonPropertyName("rowId")]
    public uint? RowId { get; set; }

    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Resolved game-relative EXD page path, e.g. <c>exd/enpcresident_0_en.exd</c>.
    /// Derived from paging and may change between patches; not part of identity.
    /// </summary>
    [JsonPropertyName("exdPath")]
    public string? ExdPath { get; set; }

    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Sheet)
        && RowId.HasValue
        && !string.IsNullOrWhiteSpace(Field);
}
