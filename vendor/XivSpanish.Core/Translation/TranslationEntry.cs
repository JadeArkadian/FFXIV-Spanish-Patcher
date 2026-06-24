using System.Text.Json.Serialization;

namespace XivSpanish.Translation;

/// <summary>
/// A translation row as the patcher consumes it. Only the fields the pipeline reads are modelled —
/// <c>source</c>, <c>target</c>, <c>status</c> and <c>sourceKey</c>. The upstream corpus carries more
/// provenance metadata (id, hash, category, translator, reviewer, notes, context), but
/// the XivSpanish.BlobBuilder tool projects it away, so it is intentionally not modelled here.
/// </summary>
public sealed class TranslationEntry
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("sourceKey")]
    public TranslationSourceKey? SourceKey { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public static class TranslationEntryStatus
{
    public const string Approved = "approved";

    /// <summary>Gold-standard tier: a manually verified, highest-confidence translation. Packaged
    /// alongside <see cref="Approved"/>.</summary>
    public const string Gold = "gold";

    public const string Draft = "draft";
    public const string NeedsReview = "needs-review";
    public const string Rejected = "rejected";

    public const string Reviewed = "reviewed";
}
