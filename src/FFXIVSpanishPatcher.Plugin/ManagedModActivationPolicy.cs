namespace FFXIVSpanishPatcher.Plugin;

/// <summary>First installs start enabled; later builds preserve the manager-owned desired choice.</summary>
public static class ManagedModActivationPolicy
{
    public static bool DesiredAfterRegeneration(
        BuildFingerprint? lastSuccessfulFingerprint,
        bool? currentPenumbraState,
        bool? persistedState)
        => lastSuccessfulFingerprint is null
            ? true
            : persistedState ?? currentPenumbraState ?? true;

    /// <summary>Checks the verified persistent state that Penumbra can apply during game startup.</summary>
    public static bool IsSessionStateApplied(CategorySyncSnapshot snapshot, bool desiredEnabled)
        => snapshot.SessionActivationAllowed
            && snapshot.ModEnabled == desiredEnabled
            && !snapshot.UsesSessionLease;

    /// <summary>Detects a managed persistent state changed outside the manager.</summary>
    public static bool NeedsSessionRepair(
        CategorySyncSnapshot snapshot,
        bool desiredEnabled,
        RegenerationDecision? regeneration)
        => regeneration?.Kind == RegenerationDecisionKind.UpToDate
            && snapshot.SessionActivationAllowed
            && !IsSessionStateApplied(snapshot, desiredEnabled);
}
