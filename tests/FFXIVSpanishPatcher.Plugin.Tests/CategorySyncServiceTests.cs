using FFXIVSpanishPatcher.Pipeline;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class CategorySyncServiceTests
{
    [Fact]
    public async Task Refresh_UsesOnlyDefaultCollectionAndMapsStableDomains()
    {
        var transport = new FakeTransport();
        await using var service = CreateService(transport);

        var snapshot = service.Refresh("FFXIVSpanish");

        Assert.Equal(CategorySyncStatus.Ready, snapshot.Status);
        Assert.Equal("Base", snapshot.CollectionName);
        Assert.Equal(["interfaz"], snapshot.EnabledDomains);
        Assert.Equal([transport.Default.Id], transport.ReadCollections);
    }

    [Fact]
    public async Task SetEnabledDomains_WritesCompleteGroupThenReconciles()
    {
        var transport = new FakeTransport();
        await using var service = CreateService(transport);

        var snapshot = service.SetEnabledDomains("FFXIVSpanish", ["misiones", "items"], confirmBreakInheritance: false);

        Assert.Equal(CategorySyncStatus.Ready, snapshot.Status);
        Assert.Equal(["items", "misiones"], snapshot.EnabledDomains.Order());
        var write = Assert.Single(transport.Writes);
        Assert.Equal(TranslationCategoryCatalog.GroupName, write.Group);
        Assert.Equal(["Misiones", "Objetos"], write.Options);
        Assert.NotEmpty(transport.ReadCollections);
    }

    [Fact]
    public async Task ExternalPenumbraChange_ReconcilesWithoutWritingBack()
    {
        var transport = new FakeTransport();
        await using var service = CreateService(transport);
        service.Refresh("FFXIVSpanish");

        transport.SetExternal(["Misiones", "Interfaz"]);

        Assert.Equal(["interfaz", "misiones"], service.Snapshot.EnabledDomains.Order());
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task ExternalChangeForAnotherMod_IsIgnored()
    {
        var transport = new FakeTransport();
        await using var service = CreateService(transport);
        service.Refresh("FFXIVSpanish");

        transport.SetExternal(["Misiones"], "AnotherMod");

        Assert.Equal(["interfaz"], service.Snapshot.EnabledDomains);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task InheritedSettings_RequireExplicitConfirmationBeforeWriting()
    {
        var transport = new FakeTransport { Inherited = true };
        await using var service = CreateService(transport);

        var pending = service.SetEnabledDomains("FFXIVSpanish", ["misiones"], confirmBreakInheritance: false);
        Assert.Empty(transport.Writes);
        var applied = service.SetEnabledDomains("FFXIVSpanish", ["misiones"], confirmBreakInheritance: true);

        Assert.Equal(CategorySyncStatus.Inherited, pending.Status);
        Assert.Contains("confirma", pending.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CategorySyncStatus.Ready, applied.Status);
        Assert.Single(transport.Writes);
    }

    [Fact]
    public async Task RejectedWrite_KeepsReadSnapshotAndDoesNotLoop()
    {
        var transport = new FakeTransport { SetResult = 9 };
        await using var service = CreateService(transport);

        var snapshot = service.SetEnabledDomains("FFXIVSpanish", ["misiones"], confirmBreakInheritance: false);

        Assert.Equal(CategorySyncStatus.WriteRejected, snapshot.Status);
        Assert.Equal(["interfaz"], snapshot.EnabledDomains);
        Assert.Single(transport.Writes);
    }

    [Fact]
    public async Task MissingGroupOrOption_IsReportedWithoutWriting()
    {
        var groupMissing = new FakeTransport { Available = new Dictionary<string, IReadOnlyList<string>>() };
        await using var groupService = CreateService(groupMissing);
        Assert.Equal(CategorySyncStatus.CategoryGroupMissing, groupService.Refresh("FFXIVSpanish").Status);

        var optionMissing = new FakeTransport();
        optionMissing.Available[TranslationCategoryCatalog.GroupName] = ["Misiones"];
        await using var optionService = CreateService(optionMissing);
        Assert.Equal(CategorySyncStatus.CategoryOptionMissing, optionService.Refresh("FFXIVSpanish").Status);
        Assert.Empty(groupMissing.Writes);
        Assert.Empty(optionMissing.Writes);
    }

    [Fact]
    public async Task Dispose_UnsubscribesAndNeverWrites()
    {
        var transport = new FakeTransport();
        var service = CreateService(transport);

        await service.DisposeAsync();
        transport.SetExternal(["Misiones"]);

        Assert.True(transport.Disposed);
        Assert.Equal(CategorySyncStatus.Disposed, service.Snapshot.Status);
        Assert.Empty(transport.Writes);
    }

    private static CategorySyncService CreateService(FakeTransport transport)
        => new(transport, new RecordingLogger());

    private sealed class FakeTransport : IPenumbraCategoryTransport
    {
        public PenumbraCollection Default { get; } = new(Guid.NewGuid(), "Base");
        public List<Guid> ReadCollections { get; } = [];
        public List<(string Group, IReadOnlyList<string> Options)> Writes { get; } = [];
        public Dictionary<string, IReadOnlyList<string>> Available { get; set; } = new()
        {
            [TranslationCategoryCatalog.GroupName] = TranslationCategoryCatalog.All.Select(category => category.DisplayName).ToArray(),
        };
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Current { get; private set; } = new Dictionary<string, IReadOnlyList<string>>
        {
            [TranslationCategoryCatalog.GroupName] = ["Interfaz"],
        };
        public bool Inherited { get; set; }
        public int SetResult { get; set; }
        public bool Disposed { get; private set; }
        public event Action<Guid, string, bool>? ModSettingChanged;

        public PenumbraCollection? GetDefaultCollection()
        {
            ReadCollections.Add(Default.Id);
            return Default;
        }

        public PenumbraModSettingsRead GetCurrentSettings(Guid collectionId, string modDirectory)
            => new(0, true, Current, Inherited);

        public IReadOnlyDictionary<string, IReadOnlyList<string>>? GetAvailableSettings(string modDirectory) => Available;

        public int SetSettings(Guid collectionId, string modDirectory, string groupName, IReadOnlyList<string> enabledOptions)
        {
            Writes.Add((groupName, enabledOptions));
            if (SetResult != 0)
            {
                return SetResult;
            }

            Current = new Dictionary<string, IReadOnlyList<string>> { [groupName] = enabledOptions };
            Inherited = false;
            ModSettingChanged?.Invoke(collectionId, modDirectory, false);
            return 0;
        }

        public void SetExternal(IReadOnlyList<string> options, string modDirectory = "FFXIVSpanish")
        {
            Current = new Dictionary<string, IReadOnlyList<string>> { [TranslationCategoryCatalog.GroupName] = options };
            ModSettingChanged?.Invoke(Default.Id, modDirectory, false);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingLogger : IPluginDiagnosticLogger
    {
        public void Information(string message) { }
        public void Warning(string message, Exception? exception = null) { }
    }
}
