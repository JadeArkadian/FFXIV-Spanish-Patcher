using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// The translation statuses the patcher actually applies. A row in any other status (<c>rejected</c>,
/// <c>needs-review</c>, <c>draft</c>...) is never written into an EXD page, so it is excluded both
/// from the embedded blob (built by <c>tools/XivSpanish.BlobBuilder</c>) and at patch time here.
/// </summary>
/// <remarks>
/// The default set is <c>approved</c> + <c>gold</c> (the gold-standard tier); both status constants
/// live in <see cref="TranslationEntryStatus"/>. This type holds the patcher's <em>packaging policy</em>
/// (which statuses to apply), not the status vocabulary itself.
/// </remarks>
public static class PackageableStatus
{
    /// <summary>Statuses packaged by default: <c>approved</c> and <c>gold</c> (case-insensitive).</summary>
    public static readonly IReadOnlySet<string> Default = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TranslationEntryStatus.Approved,
        TranslationEntryStatus.Gold,
    };

    /// <summary>True when the entry's status is one the patcher applies.</summary>
    public static bool IsPackageable(TranslationEntry entry, IReadOnlySet<string> statuses)
        => entry.Status is { } s && statuses.Contains(s);
}
