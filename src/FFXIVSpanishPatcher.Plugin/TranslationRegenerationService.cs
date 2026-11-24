using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Serializes a verified tree build followed by the transactional Penumbra installation.</summary>
public sealed class TranslationRegenerationService(
    ITranslationTreeBuilder treeBuilder,
    IModInstaller installer,
    IPluginConfigurationStore configurationStore,
    PluginConfiguration configuration)
{
    // Working directories the session creates inside Penumbra's mod root for one regeneration.
    internal const string CandidateInfix = ".ffxivspanish-candidate-";
    internal const string PipelineStagingDirectory = ".ffxivspanish-pipeline-staging";

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
            // The pipeline removes its own run from the staging directory; do not leave the empty
            // parent behind in the user's mod root.
            DeleteCandidateBestEffort(Path.Combine(modRootPath, PipelineStagingDirectory));
        }
    }

    private static void DeleteObsoleteRootArtifacts(string modRootPath)
    {
        if (!Directory.Exists(modRootPath))
        {
            return;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modRootPath));
        var obsolete = Directory.EnumerateDirectories(root, $".{PenumbraModIdentity.CanonicalDirectory}{CandidateInfix}*")
            .Append(Path.Combine(root, PipelineStagingDirectory));
        foreach (var path in obsolete)
        {
            // Best effort: every attempt works under a fresh name, so one that cannot be removed
            // (a file still held open after the game was killed mid-generation) is not in the way.
            DeleteCandidateBestEffort(path);
        }

        // What the installer itself leaves behind. A failed candidate is kept for diagnosis only
        // until the next attempt, and each one is a full copy of the mod. A backup is deleted only
        // while the tree it was taken from is back in place: a backup with no such tree (the game
        // died mid-install) is the only copy of the previous translation.
        // Matched by name, not by a search pattern: Win32 wildcard rules differ from the plain ones.
        var leftovers = Directory.EnumerateDirectories(root)
            .Where(path => IsInstallerLeftover(root, Path.GetFileName(path)))
            .ToArray();
        foreach (var path in leftovers)
        {
            try
            {
                if (new DirectoryInfo(path).LinkTarget is null)
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Reclaiming disk space must never stop the regeneration that is about to run.
            }
        }
    }

    private static bool IsInstallerLeftover(string root, string name)
    {
        if (!name.StartsWith('.'))
        {
            return false;
        }

        if (name.Contains(ModInstaller.FailedInfix, StringComparison.Ordinal)
            || name.Contains(ModInstaller.StageInfix, StringComparison.Ordinal))
        {
            return true;
        }

        // ".FFXIVSpanish.ffxivspanish-backup-TOKEN" was taken from the tree "FFXIVSpanish".
        var backup = name.LastIndexOf(ModInstaller.BackupInfix, StringComparison.Ordinal);
        return backup > 1 && Directory.Exists(Path.Combine(root, name[1..backup]));
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
            // The installer has usually renamed the candidate away; one left behind is never
            // registered with Penumbra, and the next regeneration removes it by name.
        }
    }
}
