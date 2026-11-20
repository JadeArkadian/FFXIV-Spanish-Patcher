namespace FFXIVSpanishPatcher.Plugin;

public enum RegenerationDecisionKind
{
    FirstInstallation,
    InstallationMissing,
    InstallationMissingAutoRepairDisabled,
    UpToDate,
    RegenerationRequired,
    AutoRegenerationDisabled,
}

public sealed record RegenerationDecision(RegenerationDecisionKind Kind, string Message)
{
    public bool RequiresBuild => Kind is
        RegenerationDecisionKind.InstallationMissing or
        RegenerationDecisionKind.RegenerationRequired;
}

/// <summary>Determines regeneration from stable inputs only; timestamps and category choices never trigger a build.</summary>
public static class RegenerationPlanner
{
    public static RegenerationDecision Decide(
        BuildFingerprint desired,
        BuildFingerprint? previous,
        bool autoRegenerationEnabled,
        bool installationMissing)
    {
        if (previous is null)
        {
            return new RegenerationDecision(RegenerationDecisionKind.FirstInstallation,
                "La traducción aún no se ha instalado; requiere una instalación inicial confirmada por el usuario.");
        }

        if (installationMissing)
        {
            return autoRegenerationEnabled
                ? new RegenerationDecision(RegenerationDecisionKind.InstallationMissing,
                    "La instalación gestionada ha desaparecido; se reinstalará la traducción.")
                : new RegenerationDecision(RegenerationDecisionKind.InstallationMissingAutoRepairDisabled,
                    "La instalación gestionada ha desaparecido; reinstala la traducción manualmente.");
        }

        if (!desired.RequiresRegenerationComparedTo(previous))
        {
            return new RegenerationDecision(RegenerationDecisionKind.UpToDate,
                "La traducción instalada coincide con el juego, el corpus y el plugin actuales.");
        }

        return autoRegenerationEnabled
            ? new RegenerationDecision(RegenerationDecisionKind.RegenerationRequired,
                "El juego, el corpus o el plugin han cambiado; se regenerará la traducción.")
            : new RegenerationDecision(RegenerationDecisionKind.AutoRegenerationDisabled,
                "Hay una actualización disponible, pero la regeneración automática está desactivada.");
    }
}
