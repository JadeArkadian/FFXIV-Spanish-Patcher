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

    /// <summary>
    /// The mod is switched on or off from two places, Penumbra and this manager, and both must
    /// agree. A change the user makes in Penumbra is adopted, never reverted. The one exception
    /// is the manager's own regeneration, which turns the mod off while it replaces the files:
    /// that transient state is not the user's choice.
    /// </summary>
    public static bool ShouldAdoptPenumbraState(
        CategorySyncSnapshot snapshot,
        bool? persistedState,
        bool regenerationRunning)
        => !regenerationRunning
            && snapshot.CanEdit
            && snapshot.ModEnabled is { } enabled
            && persistedState != enabled;

    /// <summary>
    /// State a verified installation must have when it is reconciled. Penumbra holds the live
    /// truth, including changes made while the manager was not running. Only when the manager
    /// itself still owes the user's choice (it disabled the mod to regenerate and has not restored
    /// it yet) does the persisted choice win.
    /// </summary>
    public static bool DesiredAtReconciliation(
        bool activationPending,
        bool? persistedState,
        bool? currentPenumbraState)
        => activationPending
            ? persistedState ?? currentPenumbraState ?? true
            : currentPenumbraState ?? persistedState ?? true;
}
