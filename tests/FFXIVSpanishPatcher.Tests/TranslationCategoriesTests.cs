using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.Translation;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class TranslationCategoriesTests
{
    private static TranslationEntry ForSheet(string sheet)
        => new() { SourceKey = new TranslationSourceKey { Sheet = sheet, RowId = 1u } };

    [Theory]
    [InlineData("Addon", "interfaz")]
    [InlineData("Item", "items")]
    [InlineData("Action", "acciones")]
    [InlineData("Quest", "misiones")]
    [InlineData("PlaceName", "nombres")]
    public void DomainOf_MapsCuratedSheets(string sheet, string expectedDomain)
        => Assert.Equal(expectedDomain, TranslationCategories.DomainOf(ForSheet(sheet)));

    [Fact]
    public void DomainOf_UnknownSheet_FallsBackToSheetName()
        => Assert.Equal("SomeNewSheet", TranslationCategories.DomainOf(ForSheet("SomeNewSheet")));

    [Fact]
    public void IsSelected_NullSelection_IncludesEverything()
        => Assert.True(TranslationCategories.IsSelected(ForSheet("Item"), selected: null));

    [Fact]
    public void IsSelected_RespectsSelection_CaseInsensitive()
    {
        var selection = TranslationCategories.BuildSelection(["ITEMS"]);

        Assert.True(TranslationCategories.IsSelected(ForSheet("Item"), selection));
        Assert.False(TranslationCategories.IsSelected(ForSheet("Quest"), selection));
    }
}
