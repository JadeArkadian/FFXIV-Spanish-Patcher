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
/// Label symbols stay within Latin-1 plus the check mark: Dalamud's default font draws any
/// glyph it lacks as "=", which is how the former restart arrow reached the screen.
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
            return new(ManagedModHeaderStatus.RestartRequired, "! REINICIO NECESARIO");
        }

        if (state.Phase is PluginPhase.Checking or PluginPhase.Generating or PluginPhase.Installing or PluginPhase.Reloading)
        {
            return new(ManagedModHeaderStatus.Regenerating, "… REGENERANDO");
        }

        if (configuration?.LastSuccessfulFingerprint is null || installationMissing)
        {
            return new(ManagedModHeaderStatus.NotInstalled, "× NO INSTALADA");
        }

        // After the two checks above: a restart cannot load a translation that is still being
        // written or is not installed, so it must not hide either state.
        if (translationRestartRequired)
        {
            return new(ManagedModHeaderStatus.RestartRequired, "! REINICIO NECESARIO");
        }

        if (configuration.ManagedModEnabled == false)
        {
            return new(ManagedModHeaderStatus.Disabled, "· TRADUCCIÓN DESACTIVADA");
        }

        if (!startupWait.IsReady || !penumbra.IsReady || !categories.SessionActivationAllowed)
        {
            return new(ManagedModHeaderStatus.Blocked, "× TRADUCCIÓN BLOQUEADA");
        }

        if (categories.ModEnabled != true || categories.UsesSessionLease)
        {
            return new(ManagedModHeaderStatus.Blocked, "× TRADUCCIÓN BLOQUEADA");
        }

        if (IsBestEffort(currentFingerprint))
        {
            return new(ManagedModHeaderStatus.BestEffort, "~ ACTIVA · BEST EFFORT");
        }

        return new(ManagedModHeaderStatus.Active, "✓ TRADUCCIÓN ACTIVA");
    }

    private static bool IsBestEffort(BuildFingerprint? fingerprint)
        => fingerprint is not null
            && !GameVersionText.IsUnknown(fingerprint.InstalledGameVersion)
            && !GameVersionText.IsUnknown(fingerprint.RecommendedGameVersion)
            && !StringComparer.Ordinal.Equals(
                fingerprint.InstalledGameVersion,
                fingerprint.RecommendedGameVersion);
}
