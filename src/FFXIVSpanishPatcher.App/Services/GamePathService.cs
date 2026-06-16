using XivSpanish.GameData;

namespace FFXIVSpanishPatcher.App.Services;

/// <summary>
/// Best-effort auto-detection of the FFXIV install path. F3 reuses the upstream
/// <see cref="GameLocator"/> (XIVLauncher config) plus a few common install locations; F4 adds the
/// Windows registry and Steam library parsing.
/// </summary>
public static class GamePathService
{
    public static string? Detect()
    {
        var fromLauncher = GameLocator.TryFindGamePathFromXivLauncherConfig();
        if (IsValid(fromLauncher))
        {
            return fromLauncher;
        }

        return CommonPaths().FirstOrDefault(IsValid);
    }

    /// <summary>True when the path resolves to a usable sqpack directory.</summary>
    public static bool IsValid(string? path) => path is not null && GameLocator.ResolveSqpackPath(path) is not null;

    private static IEnumerable<string> CommonPaths() =>
    [
        @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn",
        @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online",
        @"C:\Program Files\Steam\steamapps\common\FINAL FANTASY XIV Online",
        @"C:\Program Files (x86)\FINAL FANTASY XIV - A Realm Reborn",
    ];
}
