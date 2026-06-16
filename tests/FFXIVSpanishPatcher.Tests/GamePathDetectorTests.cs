using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class GamePathDetectorTests
{
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
}
