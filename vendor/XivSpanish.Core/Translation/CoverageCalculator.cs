namespace XivSpanish.Translation;

/// <summary>
/// Coverage of one domain: approved unique strings over extractable unique strings.
/// </summary>
/// <param name="Domain">Domain label (see <see cref="DomainMap"/>).</param>
/// <param name="ApprovedUnique">Numerator: distinct approved strings that are also extractable.</param>
/// <param name="ExtractableUnique">Denominator: distinct extractable strings (after dedup).</param>
public sealed record DomainCoverage(string Domain, int ApprovedUnique, int ExtractableUnique)
{
    /// <summary>Coverage ratio in [0, 1]; 0 when there is nothing extractable.</summary>
    public double Ratio => ExtractableUnique == 0 ? 0d : (double)ApprovedUnique / ExtractableUnique;
}

/// <summary>
/// Full coverage report: one row per domain plus an explicit global total. Numerator and
/// denominator are always exposed so the ratio is auditable.
/// </summary>
public sealed record CoverageReport(IReadOnlyList<DomainCoverage> Domains, DomainCoverage Global);

/// <summary>
/// Computes translation coverage as <c>approved únicos / total extraíbles únicos</c>, broken
/// down by domain and globally.
/// </summary>
/// <remarks>
/// <para>
/// Denominator source: extractor candidates/manifests (<c>data/candidates/*.jsonl</c>).
/// Numerator source: translations with <c>status=approved</c> (<c>data/translations/*.jsonl</c>).
/// </para>
/// <para>
/// Uniqueness criterion (identical for numerator and denominator): a string is counted once
/// per domain, keyed by <see cref="TextHasher.Hash"/> of its <c>source</c> (the same normalized
/// hash the extractor emits). So if one source string appears in N rows it counts once.
/// </para>
/// <para>
/// The numerator counts only approved strings that are <em>also</em> extractable in the same
/// domain (intersection with the denominator set), so per-domain coverage never exceeds 100%.
/// </para>
/// </remarks>
public static class CoverageCalculator
{
    public static CoverageReport Compute(
        IEnumerable<TranslationEntry> candidates,
        IEnumerable<TranslationEntry> translations,
        DomainMap? domainMap = null,
        string approvedStatus = TranslationEntryStatus.Approved)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(translations);
        var map = domainMap ?? DomainMap.Sprint2Default;

        // Denominator: distinct source keys per domain across all extractable candidates.
        var extractable = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in candidates)
        {
            var domain = map.Resolve(entry);
            UniqueSet(extractable, domain).Add(UniquenessKey(entry));
        }

        // Numerator: distinct approved source keys per domain, restricted to keys that are
        // also extractable in that domain so coverage stays bounded by the denominator.
        var approved = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in translations)
        {
            if (!string.Equals(entry.Status, approvedStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var domain = map.Resolve(entry);
            var key = UniquenessKey(entry);
            if (extractable.TryGetValue(domain, out var denomSet) && denomSet.Contains(key))
            {
                UniqueSet(approved, domain).Add(key);
            }
        }

        var domains = extractable.Keys
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .Select(domain => new DomainCoverage(
                domain,
                approved.TryGetValue(domain, out var num) ? num.Count : 0,
                extractable[domain].Count))
            .ToList();

        var global = new DomainCoverage(
            "TOTAL",
            domains.Sum(domain => domain.ApprovedUnique),
            domains.Sum(domain => domain.ExtractableUnique));

        return new CoverageReport(domains, global);
    }

    // Uniqueness key: normalized hash of the source string. Recomputed (not read from the
    // entry's hash field) so numerator and denominator share one deterministic criterion
    // independent of whatever hash a file happens to carry.
    private static string UniquenessKey(TranslationEntry entry) => TextHasher.Hash(entry.Source);

    private static HashSet<string> UniqueSet(Dictionary<string, HashSet<string>> map, string domain)
    {
        if (!map.TryGetValue(domain, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            map[domain] = set;
        }

        return set;
    }
}
