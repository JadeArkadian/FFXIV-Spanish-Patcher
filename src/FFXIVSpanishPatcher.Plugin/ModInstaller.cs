using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

public enum ModInstallationStep
{
    CandidateCopiedToStaging,
    ExistingModBackedUp,
    CandidatePromoted,
    PenumbraRegistered,
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
/// Promotes a complete v4 mod tree without calling Penumbra's delete API. New trees are copied to
/// a sibling staging directory, verified, then moved into place. An update retains the old tree
/// until Penumbra has accepted the reload; failures put it back and retain the failed candidate.
/// </summary>
public sealed class ModInstaller : IModInstaller
{
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
        var resolution = _identity.Resolve(request.ModRootPath, request.SelectedExistingDirectory);
        if (resolution.Kind is ModIdentityResolutionKind.Ambiguous or ModIdentityResolutionKind.Unsafe)
        {
            return new ModInstallResult(false, resolution.Message);
        }

        var adopted = resolution.Selected;
        var directoryName = adopted?.DirectoryName ?? PenumbraModIdentity.CanonicalDirectory;
        var targetPath = Path.Combine(Path.GetFullPath(request.ModRootPath), directoryName);
        if (adopted is null && Directory.Exists(targetPath))
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
        var stagePath = Path.Combine(parent, $".{directoryName}.ffxivspanish-stage-{token}");
        var backupPath = Path.Combine(parent, $".{directoryName}.ffxivspanish-backup-{token}");
        var failedPath = Path.Combine(parent, $".{directoryName}.ffxivspanish-failed-{token}");
        var backedUp = false;
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

            if (adopted is not null)
            {
                Directory.Move(targetPath, backupPath);
                backedUp = true;
                _faultInjector.Before(ModInstallationStep.ExistingModBackedUp);
                cancellationToken.ThrowIfCancellationRequested();
            }

            Directory.Move(stagePath, targetPath);
            promoted = true;
            _faultInjector.Before(ModInstallationStep.CandidatePromoted);
            cancellationToken.ThrowIfCancellationRequested();

            var apiResult = adopted is null
                ? _registry.AddMod(directoryName)
                : _registry.ReloadMod(directoryName);
            var apiStep = adopted is null ? ModInstallationStep.PenumbraRegistered : ModInstallationStep.PenumbraReloaded;
            _faultInjector.Before(apiStep);
            if (apiResult != 0)
            {
                throw new InvalidOperationException($"Penumbra devolvió el código {apiResult} al {(adopted is null ? "registrar" : "recargar")} el mod.");
            }

            if (backedUp)
            {
                try
                {
                    _faultInjector.Before(ModInstallationStep.PreviousVersionCleaned);
                    Directory.Delete(backupPath, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _log.Warning("La versión anterior se conserva porque no se pudo limpiar tras una actualización correcta.", exception);
                }
            }

            return new ModInstallResult(
                true,
                adopted is null ? "Traducción instalada en Penumbra." : "Traducción existente actualizada de forma segura.",
                directoryName,
                request.Fingerprint,
                adopted is not null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or OperationCanceledException)
        {
            RestorePreviousVersion(targetPath, backupPath, failedPath, directoryName, backedUp, promoted);
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
        string backupPath,
        string failedPath,
        string directoryName,
        bool backedUp,
        bool promoted)
    {
        try
        {
            if (promoted && Directory.Exists(targetPath))
            {
                Directory.Move(targetPath, failedPath);
            }

            if (backedUp && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, targetPath);
                var rollbackResult = _registry.ReloadMod(directoryName);
                if (rollbackResult != 0)
                {
                    _log.Warning($"Penumbra devolvió el código {rollbackResult} al recargar la versión restaurada.");
                }
            }
        }
        catch (Exception rollbackException)
        {
            _log.Warning("No se pudo completar la restauración automática; se conservaron los árboles para diagnóstico.", rollbackException);
        }
    }

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
