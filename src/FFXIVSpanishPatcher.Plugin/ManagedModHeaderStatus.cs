namespace FFXIVSpanishPatcher.Plugin;

public enum ManagedModHeaderStatus
{
    Regenerating,
    Active,
    BestEffort,
    Disabled,
    RestartRequired,
    Blocked,
    NotInstalled,
}

public sealed record ManagedModHeaderSnapshot(ManagedModHeaderStatus Status, string Label);

/// <summary>Projects one non-redundant header summary from the verified persistent state.</summary>
/// <remarks>
/// Labels are plain text. The window pairs each status with a FontAwesome icon; symbols typed into
/// a label would depend on glyphs Dalamud's default font may lack (it draws those as "=").
/// </remarks>
public static class ManagedModHeaderStatusModel
{
    public static ManagedModHeaderSnapshot Create(
        PluginStateSnapshot state,
        DalamudStartupWaitSnapshot startupWait,
        PenumbraAvailability penumbra,
        CategorySyncSnapshot categories,
        RegenerationDecision? regeneration,
        PluginConfiguration? configuration,
        BuildFingerprint? currentFingerprint,
        bool installationMissing,
        bool translationRestartRequired)
    {
        if (startupWait.Status == DalamudStartupWaitStatus.RestartRequired)
        {
            return new(ManagedModHeaderStatus.RestartRequired, "Reinicio necesario");
        }

        if (state.Phase is PluginPhase.Checking or PluginPhase.Generating)
        {
            return new(ManagedModHeaderStatus.Regenerating, "Actualizando la traducción");
        }

        if (configuration?.LastSuccessfulFingerprint is null || installationMissing)
        {
            return new(ManagedModHeaderStatus.NotInstalled, "Traducción no instalada");
        }

        // After the two checks above: a restart cannot load a translation that is still being
        // written or is not installed, so it must not hide either state.
        if (translationRestartRequired)
        {
            return new(ManagedModHeaderStatus.RestartRequired, "Reinicio necesario");
        }

        if (configuration.ManagedModEnabled == false)
        {
            return new(ManagedModHeaderStatus.Disabled, "Traducción desactivada");
        }

        if (!startupWait.IsReady || !penumbra.IsReady || !categories.SessionActivationAllowed)
        {
            return new(ManagedModHeaderStatus.Blocked, "Traducción bloqueada");
        }

        if (categories.ModEnabled != true || categories.UsesSessionLease)
        {
            return new(ManagedModHeaderStatus.Blocked, "Traducción bloqueada");
        }

        if (IsBestEffort(currentFingerprint))
        {
            return new(ManagedModHeaderStatus.BestEffort, "Traducción activa · best effort");
        }

        return new(ManagedModHeaderStatus.Active, "Traducción activa");
    }

    private static bool IsBestEffort(BuildFingerprint? fingerprint)
        => fingerprint is not null
            && !GameVersionText.IsUnknown(fingerprint.InstalledGameVersion)
            && !GameVersionText.IsUnknown(fingerprint.RecommendedGameVersion)
            && !StringComparer.Ordinal.Equals(
                fingerprint.InstalledGameVersion,
                fingerprint.RecommendedGameVersion);
}
