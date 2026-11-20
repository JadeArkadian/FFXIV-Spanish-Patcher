using XivSpanish.Translation;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class PackageableEntryTests
{
    private static TranslationEntry Entry(string? status = "approved", string target = "Hola", string sheet = "Addon", uint? rowId = 1u)
        => new() { Status = status, Target = target, SourceKey = new TranslationSourceKey { Sheet = sheet, RowId = rowId } };

    [Theory]
    [InlineData("approved", true)]
    [InlineData("gold", true)]
    [InlineData("GOLD", true)]          // case-insensitive
    [InlineData("rejected", false)]
    [InlineData("needs-review", false)]
    [InlineData("draft", false)]
    [InlineData(null, false)]
    public void HasStatus_FollowsDefaultPolicy(string? status, bool expected)
        => Assert.Equal(expected, PackageableEntry.HasStatus(Entry(status), PackageableEntry.DefaultStatuses));

    [Fact]
    public void DefaultStatuses_AreApprovedAndGold()
    {
        Assert.Contains(TranslationEntryStatus.Approved, PackageableEntry.DefaultStatuses);
        Assert.Contains(TranslationEntryStatus.Gold, PackageableEntry.DefaultStatuses);
        Assert.Equal(2, PackageableEntry.DefaultStatuses.Count);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]          // blank text would overwrite the game's string with nothing visible
    [InlineData("\t\n", false)]
    [InlineData("Hola", true)]
    public void HasContent_RequiresATargetWithVisibleText(string target, bool expected)
        => Assert.Equal(expected, PackageableEntry.HasContent(Entry(target: target)));

    [Fact]
    public void HasContent_RequiresSheetAndRow()
    {
        Assert.False(PackageableEntry.HasContent(Entry(sheet: " ")));
        Assert.False(PackageableEntry.HasContent(Entry(rowId: null)));
        Assert.False(PackageableEntry.HasContent(new TranslationEntry { Target = "Hola" }));
    }

    [Fact]
    public void IsPackageable_NeedsBothStatusAndContent()
    {
        Assert.True(PackageableEntry.IsPackageable(Entry(), PackageableEntry.DefaultStatuses));
        Assert.False(PackageableEntry.IsPackageable(Entry(status: "draft"), PackageableEntry.DefaultStatuses));
        Assert.False(PackageableEntry.IsPackageable(Entry(target: " "), PackageableEntry.DefaultStatuses));
    }
}
