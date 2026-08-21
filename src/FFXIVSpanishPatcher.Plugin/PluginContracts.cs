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

public sealed record ModInstallRequest(string PayloadId);

public sealed record ModInstallResult(bool Succeeded, string? Message);

public sealed record GameDataBuildInput(string InstalledGameVersion, string CorpusSha256);
