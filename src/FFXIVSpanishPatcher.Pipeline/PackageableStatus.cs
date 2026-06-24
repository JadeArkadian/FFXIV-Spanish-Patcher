using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// The translation statuses the patcher actually applies. A row in any other status (<c>rejected</c>,
/// <c>needs-review</c>, <c>draft</c>...) is never written into an EXD page, so it is excluded both
/// from the embedded blob (<c>build/build-translations.py</c>) and at patch time here.
/// </summary>
/// <remarks>
/// <c>gold</c> is upstream's gold-standard tier (a manually verified, highest-confidence
/// translation); it ships alongside <c>approved</c>. The constant lives here, not in the vendored
/// <see cref="TranslationEntryStatus"/>, because the patcher owns this packaging policy.
/// </remarks>
public static class PackageableStatus
{
    /// <summary>Upstream gold-standard status (not present in the vendored status constants).</summary>
    public const string Gold = "gold";

    /// <summary>Statuses packaged by default: <c>approved</c> and <c>gold</c> (case-insensitive).</summary>
    public static readonly IReadOnlySet<string> Default =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TranslationEntryStatus.Approved, Gold };

    /// <summary>True when the entry's status is one the patcher applies.</summary>
    public static bool IsPackageable(TranslationEntry entry, IReadOnlySet<string> statuses)
        => entry.Status is { } s && statuses.Contains(s);
}
