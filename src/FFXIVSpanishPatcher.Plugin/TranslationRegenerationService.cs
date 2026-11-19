using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Serializes a verified tree build followed by the transactional Penumbra installation.</summary>
public sealed class TranslationRegenerationService(
    ITranslationTreeBuilder treeBuilder,
    IModInstaller installer,
    IPluginConfigurationStore configurationStore,
    PluginConfiguration configuration)
{
    public async Task<ModInstallResult> RegenerateAsync(
        TreeBuildRequest buildRequest,
        string modRootPath,
        bool enableInDefaultCollection,
        CancellationToken cancellationToken)
    {
        try
        {
            DeleteObsoleteRootArtifacts(modRootPath);
            var build = await treeBuilder.BuildAsync(buildRequest, cancellationToken).ConfigureAwait(false);
            if (!build.Success || String.IsNullOrWhiteSpace(build.OutputPath))
            {
                return new ModInstallResult(false, $"No se pudo generar el árbol verificable ({build.Outcome}).");
            }

            // The installer switches the mod off while it replaces the files. Record that the
            // user's choice is owed before it can happen, so even a crash cannot lose it.
            configuration.ActivationPending = true;
            configurationStore.Save(configuration);
            var result = await installer.InstallAsync(
                    new ModInstallRequest(
                        build.OutputPath,
                        modRootPath,
                        buildRequest.Fingerprint,
                        enableInDefaultCollection),
                    cancellationToken)
                .ConfigureAwait(false);
            BuildFingerprintPersistence.SaveIfSuccessful(configurationStore, configuration, result);
            return result;
        }
        finally
        {
            DeleteCandidateBestEffort(buildRequest.CandidateTreePath);
        }
    }

    private static void DeleteObsoleteRootArtifacts(string modRootPath)
    {
        if (!Directory.Exists(modRootPath))
        {
            return;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modRootPath));
        var obsolete = Directory.EnumerateDirectories(root, $".{PenumbraModIdentity.CanonicalDirectory}.ffxivspanish-candidate-*")
            .Append(Path.Combine(root, ".ffxivspanish-pipeline-staging"));
        foreach (var path in obsolete)
        {
            if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null)
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private static void DeleteCandidateBestEffort(string candidateTreePath)
    {
        try
        {
            if (Directory.Exists(candidateTreePath) && new DirectoryInfo(candidateTreePath).LinkTarget is null)
            {
                Directory.Delete(candidateTreePath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temporary candidate outside Penumbra's mod root is never a usable installed mod.
        }
    }
}
