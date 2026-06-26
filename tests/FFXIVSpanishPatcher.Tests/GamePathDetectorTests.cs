using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class GamePathDetectorTests
{
    private const string GameVersion = "2026.06.18.0000.0000";

    [Fact]
    public void ParseSteamLibraryFolders_ExtractsAndUnescapesPaths()
    {
        const string vdf = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"apps" { "39210" "12345" }
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        	}
        }
        """;

        var paths = GamePathDetector.ParseSteamLibraryFolders(vdf).ToArray();

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], paths);
    }

    [Fact]
    public void ParseSteamLibraryFolders_NoPaths_ReturnsEmpty()
        => Assert.Empty(GamePathDetector.ParseSteamLibraryFolders("\"libraryfolders\" { }"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\does\not\exist\anywhere")]
    public void IsValid_RejectsNonGamePaths(string? path)
        => Assert.False(GamePathDetector.IsValid(path));

    [Fact]
    public void TryReadGameVersion_ReadsFromValidInstallRoot()
    {
        using var install = TempGameInstall();

        var version = GamePathDetector.TryReadGameVersion(install.Root);

        Assert.Equal(GameVersion, version);
    }

    [Fact]
    public void TryReadGameVersion_ReadsFromSqpackPath()
    {
        using var install = TempGameInstall();

        var version = GamePathDetector.TryReadGameVersion(install.Sqpack);

        Assert.Equal(GameVersion, version);
    }

    [Fact]
    public void TryReadGameVersion_InvalidPath_ReturnsNull()
        => Assert.Null(GamePathDetector.TryReadGameVersion(@"C:\does\not\exist\anywhere"));

    private static TempInstall TempGameInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffxivsp-game-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var sqpack = Path.Combine(game, "sqpack");
        Directory.CreateDirectory(Path.Combine(sqpack, "ffxiv"));
        File.WriteAllText(Path.Combine(game, "ffxivgame.ver"), GameVersion + Environment.NewLine);
        return new TempInstall(root, sqpack);
    }

    private sealed class TempInstall(string root, string sqpack) : IDisposable
    {
        public string Root { get; } = root;

        public string Sqpack { get; } = sqpack;

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
