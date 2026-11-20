using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

public enum ModInstallationStep
{
    CandidateCopiedToStaging,
    ExistingModBackedUp,
    CandidatePromoted,
    PenumbraRegistered,
    PenumbraActivated,
    PenumbraReloaded,
    PreviousVersionCleaned,
}

/// <summary>Test seam used to prove that every interrupted transaction restores the old tree.</summary>
public interface IModInstallationFaultInjector
{
    void Before(ModInstallationStep step);
}

public sealed class NoOpModInstallationFaultInjector : IModInstallationFaultInjector
{
    public static NoOpModInstallationFaultInjector Instance { get; } = new();

    public void Before(ModInstallationStep step) { }
}

/// <summary>
/// Promotes a complete v4 mod tree. New trees are copied to a sibling staging directory, verified,
/// then moved into place. A regeneration consolidates any duplicate managed trees into one tree;
/// failures restore every pre-existing tree and retain the failed candidate.
/// </summary>
public sealed class ModInstaller : IModInstaller
{
    // Sibling directories this installer creates beside a mod tree, as ".{tree}{infix}{token}".
    // TranslationRegenerationService removes the ones a previous attempt left behind.
    internal const string StageInfix = ".ffxivspanish-stage-";
    internal const string FailedInfix = ".ffxivspanish-failed-";
    internal const string BackupInfix = ".ffxivspanish-backup-";

    private readonly IPenumbraModRegistry _registry;
    private readonly PenumbraModIdentityService _identity;
    private readonly IPluginDiagnosticLogger _log;
    private readonly IModInstallationFaultInjector _faultInjector;

    public ModInstaller(
        IPenumbraModRegistry registry,
        IPluginDiagnosticLogger log,
        IModInstallationFaultInjector? faultInjector = null)
    {
        _registry = registry;
        _identity = new PenumbraModIdentityService(registry);
        _log = log;
        _faultInjector = faultInjector ?? NoOpModInstallationFaultInjector.Instance;
    }

    public Task<ModInstallResult> InstallAsync(ModInstallRequest request, CancellationToken cancellationToken)
        => Task.Run(() => Install(request, cancellationToken), cancellationToken);

    private ModInstallResult Install(ModInstallRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolution = _identity.Resolve(request.ModRootPath);
        if (resolution.Kind == ModIdentityResolutionKind.Unsafe)
        {
            return new ModInstallResult(false, resolution.Message);
        }

        var existing = resolution.Candidates;
        var adopted = existing.Count > 0;
        var survivor = adopted ? ChooseSurvivor(existing) : null;
        var directoryName = survivor?.DirectoryName ?? PenumbraModIdentity.CanonicalDirectory;
        var targetPath = Path.Combine(Path.GetFullPath(request.ModRootPath), directoryName);
        if (!adopted && Directory.Exists(targetPath))
        {
            return new ModInstallResult(false,
                $"Ya existe {directoryName}, pero Penumbra no lo reconoce como una instalación segura para adoptar.");
        }

        if (Directory.Exists(request.CandidateTreePath) && new DirectoryInfo(request.CandidateTreePath).LinkTarget is not null)
        {
            return new ModInstallResult(false, "El árbol candidato no puede ser un enlace simbólico.");
        }

        var token = Guid.NewGuid().ToString("N");
        var parent = Path.GetDirectoryName(targetPath)!;
        var stagePath = Path.Combine(parent, $".{directoryName}{StageInfix}{token}");
        var failedPath = Path.Combine(parent, $".{directoryName}{FailedInfix}{token}");
        var backups = new List<ExistingTreeBackup>();
        var promoted = false;

        try
        {
            CopyTreeWithoutLinks(request.CandidateTreePath, stagePath, cancellationToken);
            _faultInjector.Before(ModInstallationStep.CandidateCopiedToStaging);
            cancellationToken.ThrowIfCancellationRequested();

            var problems = new ModTreeVerifier().Verify(stagePath);
            if (problems.Count > 0)
            {
                return new ModInstallResult(false, "El árbol candidato no supera la verificación: " + String.Join("; ", problems));
            }

            foreach (var candidate in existing)
            {
                var backupPath = Path.Combine(parent, $".{candidate.DirectoryName}{BackupInfix}{token}");
                Directory.Move(candidate.FullPath, backupPath);
                backups.Add(new ExistingTreeBackup(candidate, backupPath));
                _faultInjector.Before(ModInstallationStep.ExistingModBackedUp);
                cancellationToken.ThrowIfCancellationRequested();
            }

            Directory.Move(stagePath, targetPath);
            promoted = true;
            _faultInjector.Before(ModInstallationStep.CandidatePromoted);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var duplicate in backups.Where(backup => !StringComparer.Ordinal.Equals(backup.Original.DirectoryName, directoryName)))
            {
                // Penumbra's documented reload operation drops a mod that is no longer valid.
                // The directory has been moved transactionally, so this only removes the duplicate
                // from its in-memory list before we install the regenerated survivor.
                _registry.ReloadMod(duplicate.Original.DirectoryName);
            }

            var apiResult = !adopted
                ? _registry.AddMod(directoryName)
                : _registry.ReloadMod(directoryName);
            var apiStep = !adopted ? ModInstallationStep.PenumbraRegistered : ModInstallationStep.PenumbraReloaded;
            _faultInjector.Before(apiStep);
            if (apiResult != 0)
            {
                throw new InvalidOperationException($"Penumbra devolvió el código {apiResult} al {(!adopted ? "registrar" : "recargar")} el mod.");
            }

            var enableResult = _registry.SetEnabled(directoryName, request.EnableInDefaultCollection);
            _faultInjector.Before(ModInstallationStep.PenumbraActivated);
            // Penumbra returns NothingChanged (1) when the survivor was already enabled.
            // Both that response and Success (0) establish the requested enabled state.
            if (enableResult is not 0 and not 1)
            {
                throw new InvalidOperationException(
                    $"Penumbra devolvió el código {enableResult} al sincronizar el estado del mod en Base/Default.");
            }

            if (backups.Count > 0)
            {
                try
                {
                    _faultInjector.Before(ModInstallationStep.PreviousVersionCleaned);
                    foreach (var backup in backups)
                    {
                        Directory.Delete(backup.BackupPath, recursive: true);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _log.Warning("La versión anterior se conserva porque no se pudo limpiar tras una actualización correcta.", exception);
                }
            }

            return new ModInstallResult(
                true,
                !adopted
                    ? "Traducción instalada en Penumbra."
                    : existing.Count == 1
                        ? "Traducción existente actualizada de forma segura."
                        : $"Traducción actualizada y {existing.Count - 1} copia(s) duplicada(s) retirada(s).",
                directoryName,
                request.Fingerprint,
                adopted);
        }
        // IPC failures have their own exception hierarchy. Every managed failure after promotion
        // must follow the same rollback path, not only filesystem and validation exceptions.
        catch (Exception exception)
        {
            RestorePreviousVersion(targetPath, failedPath, directoryName, backups, promoted);
            if (exception is OperationCanceledException)
            {
                throw;
            }

            _log.Warning("La instalación no se completó; se conservó la versión anterior cuando existía.", exception);
            return new ModInstallResult(false, $"No se pudo instalar la traducción: {exception.Message}");
        }
        finally
        {
            if (Directory.Exists(stagePath))
            {
                Directory.Delete(stagePath, recursive: true);
            }
        }
    }

    private void RestorePreviousVersion(
        string targetPath,
        string failedPath,
        string directoryName,
        IReadOnlyList<ExistingTreeBackup> backups,
        bool promoted)
    {
        try
        {
            if (promoted && Directory.Exists(targetPath))
            {
                Directory.Move(targetPath, failedPath);
            }

            foreach (var backup in backups.Reverse())
            {
                if (Directory.Exists(backup.BackupPath) && !Directory.Exists(backup.Original.FullPath))
                {
                    Directory.Move(backup.BackupPath, backup.Original.FullPath);
                }
            }

            foreach (var backup in backups)
            {
                var rollbackResult = _registry.ReloadMod(backup.Original.DirectoryName);
                if (rollbackResult != 0)
                {
                    var addResult = _registry.AddMod(backup.Original.DirectoryName);
                    if (addResult != 0)
                    {
                        _log.Warning($"Penumbra devolvió los códigos {rollbackResult}/{addResult} al restaurar {backup.Original.DirectoryName}.");
                    }
                }
            }
        }
        catch (Exception rollbackException)
        {
            _log.Warning("No se pudo completar la restauración automática; se conservaron los árboles para diagnóstico.", rollbackException);
        }
    }

    private static ModIdentityCandidate ChooseSurvivor(IReadOnlyList<ModIdentityCandidate> candidates)
        // The directory name is never used to establish identity. Once all candidates have passed
        // the stable identity checks, it provides a deterministic preference for consolidation.
        => candidates
            .OrderByDescending(candidate => StringComparer.Ordinal.Equals(candidate.DirectoryName, PenumbraModIdentity.CanonicalDirectory))
            .ThenBy(candidate => candidate.DirectoryName, StringComparer.Ordinal)
            .First();

    private sealed record ExistingTreeBackup(ModIdentityCandidate Original, string BackupPath);

    private static void CopyTreeWithoutLinks(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"No existe el árbol candidato: {sourcePath}");
        }

        if (Directory.Exists(destinationPath))
        {
            throw new IOException($"Ya existe el directorio temporal de instalación: {destinationPath}");
        }

        Directory.CreateDirectory(destinationPath);
        var pending = new Queue<(string Source, string Destination)>();
        pending.Enqueue((sourcePath, destinationPath));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (source, destination) = pending.Dequeue();
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
            {
                if (new DirectoryInfo(directory).LinkTarget is not null)
                {
                    throw new InvalidDataException($"El árbol candidato contiene un directorio enlazado: {directory}");
                }

                var childDestination = Path.Combine(destination, Path.GetFileName(directory));
                Directory.CreateDirectory(childDestination);
                pending.Enqueue((directory, childDestination));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (new FileInfo(file).LinkTarget is not null)
                {
                    throw new InvalidDataException($"El árbol candidato contiene un archivo enlazado: {file}");
                }

                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }
    }
}
