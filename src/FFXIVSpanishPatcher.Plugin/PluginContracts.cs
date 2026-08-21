namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Stage-2 boundary for Penumbra; concrete IPC arrives in stage 3.</summary>
public interface IPenumbraIpcClient : IAsyncDisposable
{
    Task<PenumbraAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    event Action<PenumbraAvailability>? AvailabilityChanged;
}

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
}

/// <summary>IPC boundary for the single Penumbra collection and category group managed by this plugin.</summary>
public interface IPenumbraCategoryTransport : IDisposable
{
    event Action<Guid, string, bool>? ModSettingChanged;

    PenumbraCollection? GetDefaultCollection();

    PenumbraModSettingsRead GetCurrentSettings(Guid collectionId, string modDirectory);

    IReadOnlyDictionary<string, IReadOnlyList<string>>? GetAvailableSettings(string modDirectory);

    int SetSettings(Guid collectionId, string modDirectory, string groupName, IReadOnlyList<string> enabledOptions);
}

public sealed record PenumbraCollection(Guid Id, string Name);

public sealed record PenumbraModSettingsRead(
    int ResultCode,
    bool Enabled,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Options,
    bool Inherited);

/// <summary>Stage-2 boundary for Dalamud-owned game data; it never owns or disposes GameData.</summary>
public interface IGameDataBackend
{
    Task<GameDataBuildInput> GetBuildInputAsync(CancellationToken cancellationToken);
}

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
    string? SelectedExistingDirectory = null);

public sealed record ModInstallResult(
    bool Succeeded,
    string Message,
    string? ModDirectory = null,
    BuildFingerprint? PersistedFingerprint = null,
    bool AdoptedExistingMod = false);

public sealed record GameDataBuildInput(string InstalledGameVersion, string CorpusSha256);
