using System.Text.Json.Serialization;

namespace XivSpanish.Translation;

/// <summary>
/// One baseline tuple: the stable identity of an extractable row plus the normalized hash of
/// its source text. The baseline manifest is a small, deterministic list of these — never the
/// source text itself, so no <c>.exd</c>/<c>.exh</c> or large dump is versioned (AGENTS.md).
/// </summary>
/// <remarks>
/// Identity is <c>(sheet, rowId, subRowId, field)</c> derived from the entry's
/// <see cref="TranslationSourceKey"/>. <c>rowId</c> is kept as its own field per the acceptance
/// criterion <c>(sheet, rowId, hash)</c>; the full <see cref="Key"/> string carries the rest of
/// the identity so distinct sub-rows/fields never collide.
/// </remarks>
public sealed class SnapshotEntry
{
    [JsonPropertyName("sheet")]
    public string Sheet { get; set; } = string.Empty;

    [JsonPropertyName("rowId")]
    public uint? RowId { get; set; }

    [JsonPropertyName("subRowId")]
    public uint? SubRowId { get; set; }

    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Deterministic identity string used for diffing. Built from the identity tuple with a
    /// separator that cannot appear inside the parts, so it is collision-free.
    /// </summary>
    [JsonIgnore]
    public string Key => BuildKey(Sheet, RowId, SubRowId, Field);

    // Unit separator (U+001F): a control char that cannot appear in sheet/field text,
    // so the joined identity key is unambiguous and two distinct identities never collide.
    private const char Sep = '';

    internal static string BuildKey(string sheet, uint? rowId, uint? subRowId, string field)
        => string.Join(
            Sep,
            sheet ?? string.Empty,
            rowId?.ToString() ?? string.Empty,
            subRowId?.ToString() ?? string.Empty,
            field ?? string.Empty);
}

/// <summary>A single row that changed its hash between baseline and re-extraction.</summary>
public sealed record ChangedRow(SnapshotEntry Baseline, SnapshotEntry Current);

/// <summary>
/// The diff between a baseline manifest and a re-extraction: rows added, rows whose hash
/// changed, and rows deleted. Each list is ordered deterministically by identity key.
/// </summary>
public sealed record SnapshotDiff(
    IReadOnlyList<SnapshotEntry> Added,
    IReadOnlyList<ChangedRow> Changed,
    IReadOnlyList<SnapshotEntry> Deleted);

/// <summary>
/// Builds a deterministic baseline manifest from an extraction and diffs a re-extraction
/// against a baseline. Pure and testable; reuses <see cref="TextHasher"/> for hashing so the
/// hash matches what the extractor and coverage tool already produce.
/// </summary>
public static class SnapshotDiffer
{
    /// <summary>
    /// Projects extractor entries into a deterministic baseline manifest. Only entries with a
    /// complete <see cref="TranslationSourceKey"/> (sheet + rowId + field) are kept — an
    /// incomplete key cannot be a stable identity. The result is sorted by identity key, so the
    /// same input always yields byte-identical output (acceptance criterion: deterministic).
    /// </summary>
    public static IReadOnlyList<SnapshotEntry> BuildBaseline(IEnumerable<TranslationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Last write wins per identity, so a manifest carrying duplicate rows is collapsed
        // deterministically rather than emitting two tuples for the same identity.
        var byKey = new Dictionary<string, SnapshotEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = entry.SourceKey;
            if (key is null || !key.IsComplete)
            {
                continue;
            }

            var snapshot = new SnapshotEntry
            {
                Sheet = key.Sheet,
                RowId = key.RowId,
                SubRowId = key.SubRowId,
                Field = key.Field,
                Hash = TextHasher.Hash(entry.Source),
            };
            byKey[snapshot.Key] = snapshot;
        }

        return byKey.Values
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Diffs a re-extraction against a baseline manifest by identity key:
    /// <list type="bullet">
    /// <item><b>Added</b>: key present now, absent in baseline.</item>
    /// <item><b>Changed</b>: key present in both, hash differs.</item>
    /// <item><b>Deleted</b>: key present in baseline, absent now.</item>
    /// </list>
    /// All three lists are ordered by identity key for deterministic output.
    /// </summary>
    public static SnapshotDiff Diff(
        IEnumerable<SnapshotEntry> baseline,
        IEnumerable<SnapshotEntry> current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baseByKey = Index(baseline);
        var currByKey = Index(current);

        var added = new List<SnapshotEntry>();
        var changed = new List<ChangedRow>();
        var deleted = new List<SnapshotEntry>();

        foreach (var (key, curr) in currByKey)
        {
            if (!baseByKey.TryGetValue(key, out var prev))
            {
                added.Add(curr);
            }
            else if (!string.Equals(prev.Hash, curr.Hash, StringComparison.Ordinal))
            {
                changed.Add(new ChangedRow(prev, curr));
            }
        }

        foreach (var (key, prev) in baseByKey)
        {
            if (!currByKey.ContainsKey(key))
            {
                deleted.Add(prev);
            }
        }

        added.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        changed.Sort((a, b) => string.CompareOrdinal(a.Current.Key, b.Current.Key));
        deleted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        return new SnapshotDiff(added, changed, deleted);
    }

    // Index by identity key; last occurrence wins so duplicate-bearing inputs stay deterministic.
    private static Dictionary<string, SnapshotEntry> Index(IEnumerable<SnapshotEntry> entries)
    {
        var map = new Dictionary<string, SnapshotEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            map[entry.Key] = entry;
        }

        return map;
    }
}
