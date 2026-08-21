namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Stage-2 boundary for Penumbra; concrete IPC arrives in stage 3.</summary>
public interface IPenumbraIpcClient : IAsyncDisposable
{
    Task<PenumbraAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);
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

public sealed record PenumbraAvailability(bool IsInstalled, bool IsEnabled, int BreakingVersion, int FeatureVersion, string? Message);

public sealed record ModInstallRequest(string PayloadId);

public sealed record ModInstallResult(bool Succeeded, string? Message);

public sealed record GameDataBuildInput(string InstalledGameVersion, string CorpusSha256);
