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

/// <summary>
/// The single definition of a row that can be packaged. The blob builder uses it to decide what
/// ships and the pipeline to decide what is written, so the two can never disagree: a row needs a
/// packageable status, a target with visible text and a source key that names its sheet and row.
/// </summary>
public static class PackageableEntry
{
    /// <summary>Statuses packaged by default: <c>approved</c> and <c>gold</c> (case-insensitive).</summary>
    public static readonly IReadOnlySet<string> DefaultStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TranslationEntryStatus.Approved,
        TranslationEntryStatus.Gold,
    };

    public static bool HasStatus(TranslationEntry entry, IReadOnlySet<string> statuses)
        => entry.Status is { } status && statuses.Contains(status);

    /// <summary>
    /// A non-blank target and a complete source key. An empty <em>source</em> is fine: with a
    /// target it is a valid write-at-offset entry.
    /// </summary>
    public static bool HasContent(TranslationEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Target)
           && entry.SourceKey is { } key
           && !string.IsNullOrWhiteSpace(key.Sheet)
           && key.RowId.HasValue;

    public static bool IsPackageable(TranslationEntry entry, IReadOnlySet<string> statuses)
        => HasStatus(entry, statuses) && HasContent(entry);
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
