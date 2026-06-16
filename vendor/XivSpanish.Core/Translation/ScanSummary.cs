namespace XivSpanish.Translation;

/// <summary>
/// One sheet that failed to load or enumerate during a scan, kept so failures are
/// reported instead of silently dropped.
/// </summary>
/// <param name="Sheet">Sheet name (the generated Lumina type name).</param>
/// <param name="ExceptionType">Full type name of the failure, or a short reason
/// when no exception was thrown (e.g. the typed sheet was not enumerable).</param>
/// <param name="Message">Failure message, when available.</param>
public sealed record SheetFailure(string Sheet, string ExceptionType, string? Message = null);

/// <summary>
/// Accumulates per-sheet outcomes of a scan so the extractor can report
/// <c>processed / with-records / failed</c> counts and, in verbose mode, list every
/// failed sheet with its exception type. Centralizing the tally here keeps it
/// unit-testable without game data, mirroring <see cref="DeterministicMerge"/>.
/// </summary>
/// <remarks>
/// Thread-safe: the parallel scan records outcomes from worker threads, so every
/// mutating method locks. Reads (<see cref="Failures"/>, the count properties) are
/// intended for the single-threaded summary phase after the scan completes.
/// </remarks>
public sealed class ScanSummary
{
    private readonly object gate = new();
    private readonly List<SheetFailure> failures = new();
    private int processed;
    private int withRecords;

    /// <summary>Sheets the scan attempted to load (succeeded or failed).</summary>
    public int Processed
    {
        get { lock (gate) { return processed; } }
    }

    /// <summary>Sheets that produced at least one matching record.</summary>
    public int WithRecords
    {
        get { lock (gate) { return withRecords; } }
    }

    /// <summary>Sheets that failed to load or enumerate.</summary>
    public int Failed
    {
        get { lock (gate) { return failures.Count; } }
    }

    /// <summary>Snapshot of the recorded failures, ordered by sheet name.</summary>
    public IReadOnlyList<SheetFailure> Failures
    {
        get
        {
            lock (gate)
            {
                return failures
                    .OrderBy(failure => failure.Sheet, StringComparer.Ordinal)
                    .ToList();
            }
        }
    }

    /// <summary>Records a sheet that loaded and produced <paramref name="recordCount"/> records.</summary>
    public void RecordSuccess(int recordCount)
    {
        lock (gate)
        {
            processed++;
            if (recordCount > 0)
            {
                withRecords++;
            }
        }
    }

    /// <summary>Records a sheet that failed to load or enumerate. Never silent.</summary>
    public void RecordFailure(SheetFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        lock (gate)
        {
            processed++;
            failures.Add(failure);
        }
    }

    /// <summary>One-line tally for the scan summary.</summary>
    public string FormatSummary()
        => $"Scan summary: {Processed} sheets processed, {WithRecords} with records, {Failed} failed.";

    /// <summary>Per-failure lines for verbose mode, one sheet per line.</summary>
    public IEnumerable<string> FormatFailures()
        => Failures.Select(failure => string.IsNullOrEmpty(failure.Message)
            ? $"  FAILED {failure.Sheet}: {failure.ExceptionType}"
            : $"  FAILED {failure.Sheet}: {failure.ExceptionType}: {failure.Message}");
}
