using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Reads deterministic fingerprint inputs from the same resources and game installation as the plugin.</summary>
internal sealed class DalamudBuildInputProvider(Assembly assembly, IDataManager dataManager) : IGameDataBackend
{
    public Task<GameDataBuildInput> GetBuildInputAsync(CancellationToken cancellationToken)
        => Task.Run(() => Read(cancellationToken), cancellationToken);

    private GameDataBuildInput Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var corpus = assembly.GetManifestResourceStream("FFXIVSpanishTranslationManager.translations.dat")
            ?? throw new InvalidOperationException("No se encontró el corpus embebido.");
        var corpusHash = Convert.ToHexString(SHA256.HashData(corpus));
        cancellationToken.ThrowIfCancellationRequested();
        // Dalamud already owns the active Lumina instance. Prefer its data root over independent
        // launcher/Steam discovery, which is unreliable for custom and Wine installations.
        var gameVersion = GamePathDetector.TryReadGameVersion(dataManager.GameData.DataPath.FullName)
                          ?? GamePathDetector.TryReadGameVersion(GamePathDetector.Detect())
                          ?? "desconocida";
        return new GameDataBuildInput(gameVersion, corpusHash);
    }
}

public static class BuildFingerprintFactory
{
    public static BuildFingerprint Create(
        Assembly assembly,
        GameDataBuildInput input,
        string recommendedGameVersion,
        DateTimeOffset completedAtUtc)
        => new(
            SchemaVersion: 1,
            PluginAssemblyVersion: assembly.GetName().Version?.ToString() ?? "0.0.0",
            CorpusSha256: input.CorpusSha256,
            RecommendedGameVersion: recommendedGameVersion,
            InstalledGameVersion: input.InstalledGameVersion,
            CategorySchemaVersion: 1,
            PackageFormatVersion: 4,
            ModIdentity: PenumbraModIdentity.CanonicalDirectory,
            SuccessfulPayloadId: Guid.NewGuid().ToString("N"),
            CompletedAtUtc: completedAtUtc);

    public static string ReadRecommendedGameVersion(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream("FFXIVSpanishTranslationManager.recommended-game-version.txt")
            ?? throw new InvalidOperationException("No se encontró la versión recomendada embebida.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Trim();
    }
}
