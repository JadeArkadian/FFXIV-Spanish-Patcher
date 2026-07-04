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
    [InlineData("NpcYell", "misiones")]
    [InlineData("JournalGenre", "misiones")]
    [InlineData("InstanceContentTextData", "misiones")]
    [InlineData("AirshipExplorationLog", "misiones")]
    [InlineData("AnimaWeaponFUITalkParam", "misiones")]
    [InlineData("CompleteJournal", "misiones")]
    [InlineData("ContentUICategory", "misiones")]
    [InlineData("DescriptionString", "misiones")]
    [InlineData("Fate", "misiones")]
    [InlineData("Leve", "misiones")]
    [InlineData("custom/000/RegFstAdvGuild_00005", "misiones")]
    [InlineData("custom/000/RegFstAetheryteGuid_00032", "misiones")]
    [InlineData("custom/000/RegFstArcGuild_00008", "misiones")]
    [InlineData("custom/000/RegFstCnjGuild_00023", "misiones")]
    [InlineData("custom/000/RegFstCnjPreach_00024", "misiones")]
    [InlineData("custom/000/RegFstEternalCeremonyGuideHall_00017", "misiones")]
    [InlineData("custom/000/RegFstEternalCeremonyGuideRoom_00016", "misiones")]
    [InlineData("custom/000/RegFstHrvGuild_00033", "misiones")]
    [InlineData("custom/000/RegFstInnInfo_00022", "misiones")]
    [InlineData("custom/000/RegFstLncGuild_00007", "misiones")]
    [InlineData("custom/000/RegFstMagicItemTips_00045", "misiones")]
    [InlineData("custom/000/RegFstTanGuild_00030", "misiones")]
    [InlineData("custom/000/RegFstWdkGuild_00029", "misiones")]
    [InlineData("custom/000/RegSeaAcnGuild_00089", "misiones")]
    [InlineData("custom/000/RegSeaAdvGuild_00050", "misiones")]
    [InlineData("custom/000/RegSeaAetheGuid_00051", "misiones")]
    [InlineData("custom/000/RegSeaArmGuild_00056", "misiones")]
    [InlineData("TextCommand", "interfaz")]
    [InlineData("TextCommandParam", "interfaz")]
    [InlineData("ChatBubbleType", "interfaz")]
    [InlineData("CircleActivity", "interfaz")]
    [InlineData("EmjAddon", "interfaz")]
    [InlineData("EventTutorial", "interfaz")]
    [InlineData("EventTutorialPage", "interfaz")]
    [InlineData("FGSAddon", "interfaz")]
    [InlineData("FurnitureCatalogCategory", "interfaz")]
    [InlineData("GuideTitle", "interfaz")]
    [InlineData("GuildleveAssignment", "interfaz")]
    [InlineData("GuildleveAssignmentTalk", "interfaz")]
    [InlineData("GuildOrder", "interfaz")]
    [InlineData("HWDDevLevelWebText", "interfaz")]
    [InlineData("McGuffinUIData", "interfaz")]
    [InlineData("MJIDisposalShopUICategory", "interfaz")]
    [InlineData("MJIHudMode", "interfaz")]
    [InlineData("MYCTemporaryItemUICategory", "interfaz")]
    [InlineData("PerformGuideScore", "interfaz")]
    [InlineData("QuestRedoChapterUI", "interfaz")]
    [InlineData("QuestRedoChapterUICategory", "interfaz")]
    [InlineData("QuestRedoChapterUITab", "interfaz")]
    [InlineData("QuickChatTransient", "interfaz")]
    [InlineData("SpearfishingEcology", "interfaz")]
    [InlineData("SubmarineExplorationLog", "interfaz")]
    [InlineData("WarpLogic", "interfaz")]
    [InlineData("WebGuidance", "interfaz")]
    [InlineData("WKSNextPlanetGuidance", "interfaz")]
    [InlineData("WKSPraiseUI", "interfaz")]
    [InlineData("YardCatalogCategory", "interfaz")]
    [InlineData("ContentsTutorialPage", "interfaz")]
    [InlineData("FieldMarker", "interfaz")]
    [InlineData("Marker", "interfaz")]
    [InlineData("OmikujiGuidance", "interfaz")]
    [InlineData("Platform", "interfaz")]
    [InlineData("TopicSelect", "interfaz")]
    [InlineData("ConfigKey", "interfaz")]
    [InlineData("ClassJobActionUICategory", "clases")]
    [InlineData("AquariumWater", "items")]
    [InlineData("BankaCraftWorks", "items")]
    [InlineData("BuddyEquip", "items")]
    [InlineData("CabinetSubCategory", "items")]
    [InlineData("ChocoboRaceItem", "items")]
    [InlineData("CollectablesShop", "items")]
    [InlineData("CollectablesShopItemGroup", "items")]
    [InlineData("CompanyCraftDraft", "items")]
    [InlineData("CompanyCraftDraftCategory", "items")]
    [InlineData("CompanyCraftManufactoryState", "items")]
    [InlineData("CraftLeveTalk", "items")]
    [InlineData("CraftType", "items")]
    [InlineData("DeepDungeonEquipment", "items")]
    [InlineData("DisposalShop", "items")]
    [InlineData("DisposalShopFilterType", "items")]
    [InlineData("EurekaAetherItem", "items")]
    [InlineData("EurekaMagiciteItemType", "items")]
    [InlineData("FccShop", "items")]
    [InlineData("FittingShopCategory", "items")]
    [InlineData("FittingShopItemSet", "items")]
    [InlineData("Glasses", "items")]
    [InlineData("TofuBg", "items")]
    [InlineData("TofuEditParam", "items")]
    [InlineData("TofuObject", "items")]
    [InlineData("TofuObjectCategory", "items")]
    [InlineData("TofuPreset", "items")]
    [InlineData("TofuPresetCategory", "items")]
    [InlineData("Treasure", "items")]
    [InlineData("Warp", "items")]
    [InlineData("YKW", "items")]
    [InlineData("BeastReputationRank", "nombres")]
    [InlineData("DawnMemberUIParam", "nombres")]
    [InlineData("EventAction", "eventos")]
    [InlineData("EventItemCategory", "eventos")]
    [InlineData("MountTransient", "coleccionables")]
    [InlineData("CompanionTransient", "coleccionables")]
    [InlineData("OrchestrionCategory", "coleccionables")]
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
