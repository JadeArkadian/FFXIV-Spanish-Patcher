using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.Translation;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class PackageableStatusTests
{
    private static TranslationEntry WithStatus(string? status)
        => new() { Status = status, SourceKey = new TranslationSourceKey { Sheet = "Addon", RowId = 1u } };

    [Theory]
    [InlineData("approved", true)]
    [InlineData("gold", true)]
    [InlineData("GOLD", true)]          // case-insensitive
    [InlineData("rejected", false)]
    [InlineData("needs-review", false)]
    [InlineData("draft", false)]
    [InlineData(null, false)]
    public void IsPackageable_FollowsDefaultPolicy(string? status, bool expected)
        => Assert.Equal(expected, PackageableStatus.IsPackageable(WithStatus(status), PackageableStatus.Default));

    [Fact]
    public void Default_IsApprovedAndGold()
    {
        Assert.Contains(TranslationEntryStatus.Approved, PackageableStatus.Default);
        Assert.Contains(TranslationEntryStatus.Gold, PackageableStatus.Default);
        Assert.Equal(2, PackageableStatus.Default.Count);
    }
}
