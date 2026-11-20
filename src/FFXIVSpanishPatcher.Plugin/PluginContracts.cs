using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Stage-2 boundary for the transactional installer; filesystem work arrives in stage 4.</summary>
public interface IModInstaller
{
    Task<ModInstallResult> InstallAsync(ModInstallRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Small, deliberately non-destructive subset of Penumbra's mod API used by the installer.
/// There is intentionally no delete operation here: unloading this plugin never removes a mod.
/// </summary>
public interface IPenumbraModRegistry
{
    IReadOnlyDictionary<string, string> GetModList();

    int AddMod(string directoryName);

    int ReloadMod(string directoryName);

    /// <summary>Enables or disables this mod only in Penumbra's Base/Default collection.</summary>
    int SetEnabled(string directoryName, bool enabled);
}

/// <summary>IPC boundary for this mod's enabled state and category group in Base/Default.</summary>
public interface IPenumbraCategoryTransport : IDisposable
{
    event Action<Guid, string, bool>? ModSettingChanged;

    PenumbraCollection? GetDefaultCollection();

    PenumbraModSettingsRead GetCurrentSettings(Guid collectionId, string modDirectory);

    PenumbraTemporaryModSettingsRead GetTemporarySettings(Guid collectionId, string modDirectory, int key);

    IReadOnlyDictionary<string, IReadOnlyList<string>>? GetAvailableSettings(string modDirectory);

    int SetEnabled(Guid collectionId, string modDirectory, bool enabled);

    int SetSettings(Guid collectionId, string modDirectory, string groupName, IReadOnlyList<string> enabledOptions);

    int RemoveTemporarySettings(Guid collectionId, string modDirectory, int key);
}

public sealed record PenumbraCollection(Guid Id, string Name);

public sealed record PenumbraModSettingsRead(
    int ResultCode,
    bool Enabled,
    int Priority,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Options,
    bool Inherited);

public sealed record PenumbraTemporaryModSettingsRead(
    int ResultCode,
    bool Exists,
    bool ForceInherit,
    bool Enabled,
    int Priority,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Options,
    string Source);

public sealed record ManagedModSessionPreparation(
    bool Succeeded,
    bool DesiredEnabled,
    CategorySyncSnapshot Snapshot);

public interface ITranslationTreeBuilder
{
    Task<PatchResult> BuildAsync(TreeBuildRequest request, CancellationToken cancellationToken);
}

public sealed record TreeBuildRequest(string CandidateTreePath, BuildFingerprint Fingerprint);

public enum PenumbraAvailabilityStatus
{
    Ready,
    Unavailable,
    Disabled,
    InvalidModDirectory,
    IncompatibleBreakingVersion,
    InsufficientFeatureVersion,
    Faulted,
}

public sealed record PenumbraAvailability(
    PenumbraAvailabilityStatus Status,
    int BreakingVersion,
    int FeatureVersion,
    string? ModDirectory,
    string UserMessage,
    Exception? TechnicalError = null)
{
    public bool IsReady => Status == PenumbraAvailabilityStatus.Ready;
}

/// <summary>One fully generated, verified candidate tree ready to be promoted into Penumbra's root.</summary>
public sealed record ModInstallRequest(
    string CandidateTreePath,
    string ModRootPath,
    BuildFingerprint Fingerprint,
    bool EnableInDefaultCollection = true);

public sealed record ModInstallResult(
    bool Succeeded,
    string Message,
    string? ModDirectory = null,
    BuildFingerprint? PersistedFingerprint = null,
    bool AdoptedExistingMod = false);

public sealed record GameDataBuildInput(string InstalledGameVersion, string CorpusSha256);
