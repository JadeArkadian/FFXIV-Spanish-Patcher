namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Keeps startup blocking limited to work that is both automatic and able to finish before the
/// game resumes. First installation remains an explicit user action.
/// </summary>
public static class AutomaticRegenerationPolicy
{
    public static bool ShouldCompleteBeforeGameLoad(
        RegenerationDecision? decision,
        DalamudStartupWaitSnapshot startupWait,
        PenumbraAvailability penumbra)
        => decision?.RequiresBuild == true
            && startupWait.IsReady
            && penumbra.IsReady;
}
