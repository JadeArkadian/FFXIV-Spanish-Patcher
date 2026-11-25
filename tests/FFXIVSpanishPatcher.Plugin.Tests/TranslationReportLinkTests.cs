using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class TranslationReportLinkTests
{
    [Theory]
    [InlineData("reportar", true)]
    [InlineData("  Reportar ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("reportar algo", false)]
    public void IsReportCommand_MatchesOnlyTheSubcommand(string? arguments, bool expected)
        => Assert.Equal(expected, TranslationReportLink.IsReportCommand(arguments));

    [Fact]
    public void Build_FillsVersionGameAndZone()
        => Assert.Equal(
            "https://tally.so/r/1ARKzp?version=0.6.1&juego=2026.11.03.0000.0000&zona=132&origin=plugin",
            TranslationReportLink.Build(new Version(0, 6, 1, 0), "2026.11.03.0000.0000", 132));

    [Fact]
    public void Build_EscapesAndToleratesMissingValues()
        => Assert.Equal(
            "https://tally.so/r/1ARKzp?version=&juego=a%26b%20c&zona=0&origin=plugin",
            TranslationReportLink.Build(null, "a&b c", 0));
}
