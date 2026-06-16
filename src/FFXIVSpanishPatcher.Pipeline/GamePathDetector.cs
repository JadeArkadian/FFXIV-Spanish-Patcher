using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using XivSpanish.GameData;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Best-effort auto-detection of the local FFXIV install. Tries, in order: the XIVLauncher config,
/// the Windows uninstall registry, Steam library folders, then common install locations. The first
/// candidate that resolves to a usable sqpack directory wins. Lives in the pipeline (not the UI) so
/// the parsing is unit-testable without a display.
/// </summary>
public static partial class GamePathDetector
{
    /// <summary>First detected install path that resolves to a usable sqpack directory, or null.</summary>
    public static string? Detect() => Candidates().FirstOrDefault(IsValid);

    /// <summary>True when the path resolves to a usable sqpack directory (handles <c>game/sqpack</c>).</summary>
    public static bool IsValid(string? path) => path is not null && GameLocator.ResolveSqpackPath(path) is not null;

    private static IEnumerable<string?> Candidates()
    {
        yield return GameLocator.TryFindGamePathFromXivLauncherConfig();

        if (OperatingSystem.IsWindows())
        {
            foreach (var path in RegistryInstallLocations())
            {
                yield return path;
            }
        }

        foreach (var path in SteamCandidates())
        {
            yield return path;
        }

        foreach (var path in CommonPaths())
        {
            yield return path;
        }
    }

    /// <summary>Extracts the <c>"path"</c> entries from a Steam <c>libraryfolders.vdf</c>. Pure and
    /// public so it can be unit-tested without a Steam install.</summary>
    public static IEnumerable<string> ParseSteamLibraryFolders(string vdf)
    {
        foreach (Match match in SteamPathRegex().Matches(vdf))
        {
            // VDF escapes backslashes; unescape so the path is usable.
            yield return match.Groups[1].Value.Replace(@"\\", @"\");
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex SteamPathRegex();

    private static IEnumerable<string> SteamCandidates()
    {
        foreach (var steamRoot in SteamRoots())
        {
            foreach (var vdf in new[]
            {
                Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
                Path.Combine(steamRoot, "config", "libraryfolders.vdf"),
            })
            {
                if (!File.Exists(vdf))
                {
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(vdf);
                }
                catch
                {
                    continue;
                }

                foreach (var library in ParseSteamLibraryFolders(content))
                {
                    yield return Path.Combine(library, "steamapps", "common", "FINAL FANTASY XIV Online");
                }
            }
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var fromRegistry = ReadRegistryString(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            if (!string.IsNullOrWhiteSpace(fromRegistry))
            {
                yield return fromRegistry.Replace('/', '\\');
            }
        }

        yield return @"C:\Program Files (x86)\Steam";
        yield return @"C:\Program Files\Steam";
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> RegistryInstallLocations()
    {
        (RegistryHive Hive, string Path)[] roots =
        [
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        ];

        foreach (var (hive, path) in roots)
        {
            foreach (var location in ScanUninstallKeys(hive, path))
            {
                yield return location;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> ScanUninstallKeys(RegistryHive hive, string subkeyPath)
    {
        RegistryKey? root;
        try
        {
            root = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(subkeyPath);
        }
        catch
        {
            yield break;
        }

        if (root is null)
        {
            yield break;
        }

        using (root)
        {
            foreach (var name in root.GetSubKeyNames())
            {
                var install = TryReadFfxivInstall(root, name);
                if (!string.IsNullOrWhiteSpace(install))
                {
                    yield return install;
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryReadFfxivInstall(RegistryKey uninstallRoot, string subkeyName)
    {
        try
        {
            using var entry = uninstallRoot.OpenSubKey(subkeyName);
            if (entry?.GetValue("DisplayName") is string display
                && display.Contains("FINAL FANTASY XIV", StringComparison.OrdinalIgnoreCase))
            {
                return entry.GetValue("InstallLocation") as string;
            }
        }
        catch
        {
            // Unreadable key: ignore and keep scanning.
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryString(RegistryHive hive, string subkey, string name)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(subkey);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> CommonPaths() =>
    [
        @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn",
        @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online",
        @"C:\Program Files\Steam\steamapps\common\FINAL FANTASY XIV Online",
        @"C:\Program Files (x86)\FINAL FANTASY XIV - A Realm Reborn",
    ];
}
