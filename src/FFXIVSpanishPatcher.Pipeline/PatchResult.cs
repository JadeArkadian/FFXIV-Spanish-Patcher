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
}

/// <summary>Result of <see cref="PatchPipeline.Run"/>.</summary>
public sealed record PatchResult(
    PatchOutcome Outcome,
    string? OutputPath,
    int Pages,
    int Applied,
    int Missed,
    int Skipped)
{
    /// <summary>True when a usable package was produced.</summary>
    public bool Success => Outcome is PatchOutcome.Ok or PatchOutcome.PackagedWithMisses;

    internal static PatchResult Failure(PatchOutcome outcome, int skipped = 0)
        => new(outcome, null, 0, 0, 0, skipped);
}
