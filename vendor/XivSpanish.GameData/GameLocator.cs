using System.Text.Json;
using LuminaGameData = Lumina.GameData;

namespace XivSpanish.GameData;

/// <summary>
/// Resolves the local FFXIV installation and opens a Lumina <see cref="LuminaGameData"/>.
/// </summary>
public static class GameLocator
{
    /// <summary>
    /// Resolves an sqpack directory from an explicit path or the XIVLauncher config,
    /// then opens game data. Throws if no valid sqpack directory can be found.
    /// </summary>
    public static LuminaGameData Open(string? gamePathOrSqpack = null)
    {
        var resolved = gamePathOrSqpack ?? TryFindGamePathFromXivLauncherConfig();
        var sqpack = ResolveSqpackPath(resolved)
            ?? throw new DirectoryNotFoundException(
                "Could not resolve sqpack path. Pass an explicit game path or sqpack directory.");

        return new LuminaGameData(sqpack);
    }

    public static string? TryFindGamePathFromXivLauncherConfig()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "launcherConfigV3.json");

        if (!File.Exists(configPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        return document.RootElement.TryGetProperty("GamePath", out var gamePath)
            ? gamePath.GetString()
            : null;
    }

    public static string? ResolveSqpackPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var candidates = new[]
        {
            path,
            Path.Combine(path, "sqpack"),
            Path.Combine(path, "game", "sqpack"),
        };

        return candidates.FirstOrDefault(candidate =>
            Directory.Exists(candidate)
            && Directory.Exists(Path.Combine(candidate, "ffxiv")));
    }
}
