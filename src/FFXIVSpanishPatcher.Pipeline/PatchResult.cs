namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>Terminal outcome of a pipeline run.</summary>
public enum PatchOutcome
{
    /// <summary>Package built, every replacement applied.</summary>
    Ok,

    /// <summary>Package built but some replacements were missed (still a usable .pmp).</summary>
    PackagedWithMisses,

    /// <summary>No packageable entries for the selection. Nothing was written.</summary>
    NothingToPackage,

    /// <summary>Base EXD looks contaminated / already translated. Aborted before writing.</summary>
    Contaminated,

    /// <summary>One or more rows failed the SeString gate and --force-sestring was not set.</summary>
    SeStringGate,

    /// <summary>The generated package failed structural validation.</summary>
    ValidationFailed,

    /// <summary>The game data could not be opened/read (bad path, missing sqpack, ...).</summary>
    GameDataError,

    /// <summary>The package could not be written or promoted to its final location.</summary>
    OutputError,
}

/// <summary>Auditable coverage of one run. Counts refer only to selected packageable entries.</summary>
public sealed record PatchStatistics(
    int CandidateEntries = 0,
    int AppliedWrites = 0,
    int RowMisses = 0,
    int MissingSheets = 0,
    int MissingSheetEntries = 0,
    int MissingPages = 0,
    int MissingPageEntries = 0,
    int UnresolvedRows = 0,
    int UnsafeSeStringEntries = 0,
    int UnsupportedPages = 0,
    int UnsupportedPageEntries = 0,
    int PatchedPages = 0,
    int SkippedPages = 0)
{
    /// <summary>True when the verified package has less coverage than the selected manifest.</summary>
    public bool HasOmissions =>
        RowMisses > 0
        || MissingSheetEntries > 0
        || MissingPageEntries > 0
        || UnresolvedRows > 0
        || UnsafeSeStringEntries > 0
        || UnsupportedPageEntries > 0
        || SkippedPages > 0;

    /// <summary>Compatibility bridge for older UI/tests. Category exclusions are not counted.</summary>
    public int SkippedEntries =>
        RowMisses
        + MissingSheetEntries
        + MissingPageEntries
        + UnresolvedRows
        + UnsafeSeStringEntries
        + UnsupportedPageEntries;
}

/// <summary>Result of <see cref="PatchPipeline.Run"/>.</summary>
public sealed record PatchResult(
    PatchOutcome Outcome,
    string? OutputPath,
    PatchStatistics Statistics)
{
    /// <summary>True when a usable package was produced.</summary>
    public bool Success => Outcome is PatchOutcome.Ok or PatchOutcome.PackagedWithMisses;

    public int Pages => Statistics.PatchedPages;
    public int Applied => Statistics.AppliedWrites;
    public int Missed => Statistics.RowMisses;
    public int Skipped => Statistics.SkippedEntries;

    internal static PatchResult Failure(PatchOutcome outcome, PatchStatistics? statistics = null)
        => new(outcome, null, statistics ?? new PatchStatistics());
}
