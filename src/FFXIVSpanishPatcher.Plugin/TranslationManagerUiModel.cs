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
        PenumbraAvailability penumbra,
        CategorySyncSnapshot categories,
        RegenerationDecision? regeneration,
        PluginConfiguration? configuration,
        BuildFingerprint? currentFingerprint)
    {
        var operationRunning = state.Phase is PluginPhase.Checking or PluginPhase.Generating or PluginPhase.Installing or PluginPhase.Reloading;
        var installed = configuration?.LastSuccessfulFingerprint is not null;
        return new TranslationManagerUiSnapshot(
            installed ? "Regenerar traducción" : "Instalar traducción",
            penumbra.IsReady && !operationRunning,
            operationRunning,
            categories.CanEdit && !operationRunning,
            BuildDiagnostic(state, penumbra, categories, regeneration, configuration?.LastSuccessfulFingerprint, currentFingerprint));
    }

    private static string BuildDiagnostic(
        PluginStateSnapshot state,
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
            $"Penumbra: {penumbra.Status} (API {penumbra.BreakingVersion}.{penumbra.FeatureVersion})",
            $"Categorías: {categories.Status} en Base/Default ({categories.CollectionName})",
            $"Plugin: {current?.PluginAssemblyVersion ?? "sin datos"}",
            $"FFXIV instalado: {current?.InstalledGameVersion ?? "sin datos"}",
            $"FFXIV recomendado: {current?.RecommendedGameVersion ?? "sin datos"}",
            $"Corpus SHA-256: {corpus}",
            $"Última instalación: {installedFingerprint?.CompletedAtUtc.ToString("O") ?? "nunca"}",
            $"Regeneración: {regeneration?.Kind.ToString() ?? "sin comprobar"}");
    }

    private static string ShortHash(string value)
        => value.Length <= 12 ? value : value[..12] + "…";
}
