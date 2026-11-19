using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

public enum StatusTone
{
    Neutral,
    Warning,
    Error,
}

/// <summary>Pure projection consumed by the ImGui layer; it owns no Dalamud, IPC or filesystem work.</summary>
public sealed record TranslationManagerUiSnapshot(
    string PrimaryActionLabel,
    bool CanStartRegeneration,
    bool CanCancelRegeneration,
    bool CanEditCategories,
    string Diagnostic,
    string StatusMessage,
    StatusTone StatusTone,
    bool CanRestartGame,
    bool ActionRecommended);

public static class TranslationManagerUiModel
{
    public static TranslationManagerUiSnapshot Create(
        PluginStateSnapshot state,
        DalamudStartupWaitSnapshot startupWait,
        PenumbraAvailability penumbra,
        CategorySyncSnapshot categories,
        RegenerationDecision? regeneration,
        PluginConfiguration? configuration,
        BuildFingerprint? currentFingerprint,
        bool installationMissing = false,
        bool translationRestartRequired = false)
    {
        var operationRunning = state.Phase is PluginPhase.Checking or PluginPhase.Generating or PluginPhase.Installing or PluginPhase.Reloading;
        var installed = configuration?.LastSuccessfulFingerprint is not null;
        var translationPresent = installed && !installationMissing;
        var (statusMessage, statusTone) = DescribeStatus(
            state,
            regeneration,
            translationPresent,
            translationRestartRequired);
        var actionLabel = regeneration?.Kind is
            RegenerationDecisionKind.InstallationMissing or
            RegenerationDecisionKind.InstallationMissingAutoRepairDisabled
                ? "Reinstalar traducción"
                : installed ? "Regenerar traducción" : "Instalar traducción";
        return new TranslationManagerUiSnapshot(
            actionLabel,
            startupWait.IsReady && penumbra.IsReady && !operationRunning,
            operationRunning,
            startupWait.IsReady && categories.CanEdit && !operationRunning,
            BuildDiagnostic(
                state,
                startupWait,
                penumbra,
                categories,
                regeneration,
                configuration?.LastSuccessfulFingerprint,
                currentFingerprint),
            statusMessage,
            statusTone,
            // Restarting only helps when there is an installed translation to load.
            translationRestartRequired && translationPresent && !operationRunning,
            // Installing, reinstalling, updating or retrying is worth emphasising; regenerating a
            // translation that is already current is merely available.
            !translationPresent
                || regeneration is { Kind: not RegenerationDecisionKind.UpToDate }
                || state.Phase is PluginPhase.ErrorRecoverable or PluginPhase.ErrorFatal or PluginPhase.Cancelled);
    }

    /// <summary>
    /// One message for the status card, most urgent first. The outcome of the last operation and a
    /// missing installation come before a pending restart: a restart notice left by an earlier,
    /// completed regeneration must never present a cancelled or failed one as applied.
    /// </summary>
    private static (string Message, StatusTone Tone) DescribeStatus(
        PluginStateSnapshot state,
        RegenerationDecision? regeneration,
        bool translationPresent,
        bool translationRestartRequired)
    {
        if (state.Phase is PluginPhase.ErrorRecoverable or PluginPhase.ErrorFatal)
        {
            return (state.Message, StatusTone.Error);
        }

        if (state.Phase is PluginPhase.Cancelled)
        {
            return (translationPresent
                    ? "Operación cancelada. La traducción instalada no ha cambiado."
                    : "Operación cancelada. La traducción no está instalada.",
                StatusTone.Warning);
        }

        if (!translationPresent && regeneration is not null)
        {
            return (regeneration.Message, StatusTone.Neutral);
        }

        if (translationRestartRequired && translationPresent)
        {
            return ("Hay cambios pendientes de aplicar. Reinicia el juego para cargar desde el inicio el estado actual de la traducción.",
                StatusTone.Warning);
        }

        return (regeneration?.Message ?? state.Message, StatusTone.Neutral);
    }

    private static string BuildDiagnostic(
        PluginStateSnapshot state,
        DalamudStartupWaitSnapshot startupWait,
        PenumbraAvailability penumbra,
        CategorySyncSnapshot categories,
        RegenerationDecision? regeneration,
        BuildFingerprint? installedFingerprint,
        BuildFingerprint? currentFingerprint)
    {
        var current = currentFingerprint ?? installedFingerprint;
        var corpus = current is null ? "sin datos" : ShortHash(current.CorpusSha256);
        return string.Join(Environment.NewLine,
            "FFXIV Spanish Translation Manager",
            $"Estado: {state.Phase}",
            $"Carga temprana de Dalamud: {startupWait.Status}",
            $"Penumbra: {penumbra.Status} (API {penumbra.BreakingVersion}.{penumbra.FeatureVersion})",
            $"Categorías: {categories.Status} en Base/Default ({categories.CollectionName})",
            $"Estado persistente en Penumbra: {ActivationDiagnostic(categories)}",
            $"Plugin: {current?.PluginAssemblyVersion ?? "sin datos"}",
            $"FFXIV instalado: {current?.InstalledGameVersion ?? "sin datos"}",
            $"FFXIV recomendado: {current?.RecommendedGameVersion ?? "sin datos"}",
            $"Corpus SHA-256: {corpus}",
            $"Última instalación: {installedFingerprint?.CompletedAtUtc.ToString("O") ?? "nunca"}",
            $"Regeneración: {regeneration?.Kind.ToString() ?? "sin comprobar"}");
    }

    private static string ActivationDiagnostic(CategorySyncSnapshot categories)
        => !categories.SessionActivationAllowed
            ? "bloqueada hasta validar"
            : categories.UsesSessionLease
                ? "pendiente de migrar desde un ajuste temporal"
                : categories.ModEnabled == true
                    ? "activa para el próximo arranque"
                    : "validada y desactivada";

    private static string ShortHash(string value)
        => value.Length <= 12 ? value : value[..12] + "…";
}
