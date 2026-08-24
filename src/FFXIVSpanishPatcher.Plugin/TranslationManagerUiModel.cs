using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

public enum TranslationManagerTab
{
    Inicio,
    Categorias,
    Actualizaciones,
    Ajustes,
}

/// <summary>Pure projection consumed by the ImGui layer; it owns no Dalamud, IPC or filesystem work.</summary>
public sealed record TranslationManagerUiSnapshot(
    string PrimaryActionLabel,
    bool CanStartRegeneration,
    bool CanCancelRegeneration,
    bool CanEditCategories,
    string Diagnostic);

public static class TranslationManagerUiModel
{
    public static TranslationManagerUiSnapshot Create(
        PluginStateSnapshot state,
        DalamudStartupWaitSnapshot startupWait,
        PenumbraAvailability penumbra,
        CategorySyncSnapshot categories,
        RegenerationDecision? regeneration,
        PluginConfiguration? configuration,
        BuildFingerprint? currentFingerprint)
    {
        var operationRunning = state.Phase is PluginPhase.Checking or PluginPhase.Generating or PluginPhase.Installing or PluginPhase.Reloading;
        var installed = configuration?.LastSuccessfulFingerprint is not null;
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
                currentFingerprint));
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
