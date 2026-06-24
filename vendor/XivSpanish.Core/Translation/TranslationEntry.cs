using System.Text.Json.Serialization;

namespace XivSpanish.Translation;

public sealed class TranslationEntry
{
    private string? _context;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("context")]
    public string? Context
    {
        get => _context;
        set => _context = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("sourceKey")]
    public TranslationSourceKey? SourceKey { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("translator")]
    public string? Translator { get; set; }

    [JsonPropertyName("reviewer")]
    public string? Reviewer { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
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
