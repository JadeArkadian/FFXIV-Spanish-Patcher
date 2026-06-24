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
    // New individual domains for the post-Sprint-2 sheets.
    [InlineData("Achievement", "logros")]
    [InlineData("LogMessage", "registro")]
    [InlineData("EventItem", "eventos")]
    [InlineData("Orchestrion", "coleccionables")]
    [InlineData("TripleTriadCard", "coleccionables")]
    // Class/job names get their own toggle so users can keep them in English.
    [InlineData("ClassJob", "clases")]
    [InlineData("ClassJobCategory", "clases")]
    // Long-tail sheets folded into the broader existing buckets.
    [InlineData("FishParameter", "items")]
    [InlineData("ActionTransient", "acciones")]
    [InlineData("Title", "nombres")]
    [InlineData("JournalGenre", "misiones")]
    [InlineData("TextCommand", "interfaz")]
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
