namespace XivSpanish.Translation;

/// <summary>
/// Merges per-partition result buffers back into a single deterministic stream.
/// <para>
/// The extractor scans sheets in parallel, but each sheet writes into its own ordered
/// buffer indexed by the sheet's stable position. This helper concatenates those buffers
/// in partition order and truncates to a global limit, yielding exactly the same record
/// set and order a single-threaded scan would produce. Centralizing the invariant here
/// keeps it unit-testable without game data.
/// </para>
/// </summary>
public static class DeterministicMerge
{
    /// <summary>
    /// Concatenates <paramref name="partitions"/> in index order, skipping null buffers,
    /// and returns at most <paramref name="limit"/> items. A null or empty buffer
    /// contributes nothing; ordering within a buffer is preserved.
    /// </summary>
    /// <param name="partitions">
    /// Per-partition ordered buffers, indexed by the partition's stable position. Entries
    /// may be null when a partition produced no records.
    /// </param>
    /// <param name="limit">Maximum number of items to emit. Non-positive emits nothing.</param>
    public static IEnumerable<T> Merge<T>(IReadOnlyList<IReadOnlyList<T>?> partitions, int limit)
    {
        ArgumentNullException.ThrowIfNull(partitions);

        if (limit <= 0)
        {
            yield break;
        }

        var emitted = 0;
        foreach (var partition in partitions)
        {
            if (partition is null)
            {
                continue;
            }

            foreach (var item in partition)
            {
                yield return item;
                if (++emitted >= limit)
                {
                    yield break;
                }
            }
        }
    }
}
