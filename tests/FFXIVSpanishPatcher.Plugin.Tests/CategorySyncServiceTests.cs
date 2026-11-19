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
        Assert.True(snapshot.ModEnabled);
        Assert.Equal("Base", snapshot.CollectionName);
        Assert.Equal(["interfaz"], snapshot.EnabledDomains);
        Assert.Equal([transport.Default.Id], transport.ReadCollections);
    }

    [Fact]
    public async Task SetModEnabled_WritesBaseCollectionThenReconciles()
    {
        var transport = new FakeTransport();
        await using var service = CreateService(transport);

        var snapshot = service.SetModEnabled("FFXIVSpanish", enabled: false);

        Assert.False(snapshot.ModEnabled);
        Assert.Equal([(transport.Default.Id, "FFXIVSpanish", false)], transport.StateWrites);
    }

    [Fact]
    public async Task PrepareSession_PreservesPersistentActivationUntilValidation()
    {
        var transport = new FakeTransport { Enabled = true };
        await using var service = CreateService(transport);

        var preparation = service.PrepareSession("FFXIVSpanish", configuredDesiredState: null);

        Assert.True(preparation.Succeeded);
        Assert.True(preparation.DesiredEnabled);
        Assert.True(transport.Enabled);
        Assert.True(preparation.Snapshot.ModEnabled);
        Assert.False(preparation.Snapshot.SessionActivationAllowed);
        Assert.Empty(transport.StateWrites);
    }

    [Fact]
    public async Task GrantSessionActivation_PersistsVerifiedDesiredStateForNextBoot()
    {
        var transport = new FakeTransport { Enabled = false };
        await using var service = CreateService(transport);
        service.PrepareSession("FFXIVSpanish", configuredDesiredState: true);

        var snapshot = service.GrantSessionActivation("FFXIVSpanish", desiredEnabled: true);

        Assert.True(transport.Enabled);
        Assert.True(snapshot.ModEnabled);
        Assert.True(snapshot.SessionActivationAllowed);
        Assert.False(snapshot.UsesSessionLease);
        Assert.Equal([(transport.Default.Id, "FFXIVSpanish", true)], transport.StateWrites);
    }

    [Fact]
    public async Task EnableBeforeValidation_IsRejectedWithoutChangingPersistentState()
    {
        var transport = new FakeTransport { Enabled = false };
        await using var service = CreateService(transport);
        service.PrepareSession("FFXIVSpanish", configuredDesiredState: true);

        var snapshot = service.SetModEnabled("FFXIVSpanish", enabled: true);

        Assert.Equal(CategorySyncStatus.WriteRejected, snapshot.Status);
        Assert.False(transport.Enabled);
    }

    [Fact]
    public async Task RejectedPersistentActivation_LeavesPreviousStateUntouched()
    {
        var transport = new FakeTransport { Enabled = false, SetEnabledResult = 20 };
        await using var service = CreateService(transport);
        service.PrepareSession("FFXIVSpanish", configuredDesiredState: true);

        var snapshot = service.GrantSessionActivation("FFXIVSpanish", desiredEnabled: true);

        Assert.Equal(CategorySyncStatus.WriteRejected, snapshot.Status);
        Assert.False(snapshot.SessionActivationAllowed);
        Assert.False(transport.Enabled);
        Assert.Null(transport.Temporary);
    }

    [Fact]
    public async Task ForeignTemporarySettings_AreNeverRemovedOrOverwritten()
    {
        var transport = new FakeTransport { Enabled = true };
        transport.SetForeignTemporary("Otro plugin");
        await using var service = CreateService(transport);

        var preparation = service.PrepareSession("FFXIVSpanish", configuredDesiredState: true);
        var activation = service.GrantSessionActivation("FFXIVSpanish", desiredEnabled: true);

        Assert.False(preparation.Succeeded);
        Assert.Equal(CategorySyncStatus.WriteRejected, activation.Status);
        Assert.Equal("Otro plugin", transport.Temporary!.Source);
        Assert.True(transport.Enabled);
        Assert.Empty(transport.RemoveRequests);
    }

    [Fact]
    public async Task Dispose_RemovesOnlyLegacyOwnLeaseAndPreservesPersistentActivation()
    {
        var transport = new FakeTransport { Enabled = true };
        var service = CreateService(transport);
        service.Refresh("FFXIVSpanish");
        transport.SetOwnTemporary();
        transport.RemoveRequests.Clear();

        await service.DisposeAsync();

        var removal = Assert.Single(transport.RemoveRequests);
        Assert.Equal("FFXIVSpanish", removal.Directory);
        Assert.True(removal.Key < 0);
        Assert.True(transport.Enabled);
    }

    [Fact]
    public async Task CategoryChange_PreservesPersistentActivationWithoutTemporaryLease()
    {
        var transport = new FakeTransport { Enabled = true };
        await using var service = CreateService(transport);
        service.PrepareSession("FFXIVSpanish", configuredDesiredState: true);
        service.GrantSessionActivation("FFXIVSpanish", desiredEnabled: true);

        var snapshot = service.SetEnabledDomains("FFXIVSpanish", ["misiones", "items"], confirmBreakInheritance: false);

        Assert.True(snapshot.ModEnabled);
        Assert.False(snapshot.UsesSessionLease);
        Assert.Equal(["items", "misiones"], snapshot.EnabledDomains.Order());
    }

    [Fact]
    public async Task ExternalEnabledChange_ReconcilesWithoutWritingBack()
    {
        var transport = new FakeTransport();
        await using var service = CreateService(transport);
        service.Refresh("FFXIVSpanish");

        transport.SetExternalEnabled(false);

        Assert.False(service.Snapshot.ModEnabled);
        Assert.Empty(transport.StateWrites);
    }

    [Fact]
    public async Task RejectedEnabledChange_RestoresReadState()
    {
        var transport = new FakeTransport { SetEnabledResult = 9 };
        await using var service = CreateService(transport);

        var snapshot = service.SetModEnabled("FFXIVSpanish", enabled: false);

        Assert.Equal(CategorySyncStatus.WriteRejected, snapshot.Status);
        Assert.True(snapshot.ModEnabled);
        Assert.Single(transport.StateWrites);
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
        public List<(Guid CollectionId, string Directory, bool Enabled)> StateWrites { get; } = [];
        public List<(Guid CollectionId, string Directory, int Key)> RemoveRequests { get; } = [];
        public Dictionary<string, IReadOnlyList<string>> Available { get; set; } = new()
        {
            [TranslationCategoryCatalog.GroupName] = TranslationCategoryCatalog.All.Select(category => category.DisplayName).ToArray(),
        };
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Current { get; private set; } = new Dictionary<string, IReadOnlyList<string>>
        {
            [TranslationCategoryCatalog.GroupName] = ["Interfaz"],
        };
        public bool Inherited { get; set; }
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; } = 7;
        public int SetResult { get; set; }
        public int SetEnabledResult { get; set; }
        public int RemoveTemporaryResult { get; set; }
        public bool Disposed { get; private set; }
        public PenumbraTemporaryModSettingsRead? Temporary { get; private set; }
        public event Action<Guid, string, bool>? ModSettingChanged;

        public PenumbraCollection? GetDefaultCollection()
        {
            ReadCollections.Add(Default.Id);
            return Default;
        }

        public PenumbraModSettingsRead GetCurrentSettings(Guid collectionId, string modDirectory)
            => new(0, Enabled, Priority, Current, Inherited);

        public PenumbraTemporaryModSettingsRead GetTemporarySettings(Guid collectionId, string modDirectory, int key)
            => Temporary ?? new(0, false, false, false, 0, new Dictionary<string, IReadOnlyList<string>>(), string.Empty);

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

        public int SetEnabled(Guid collectionId, string modDirectory, bool enabled)
        {
            StateWrites.Add((collectionId, modDirectory, enabled));
            if (SetEnabledResult != 0)
            {
                return SetEnabledResult;
            }

            Enabled = enabled;
            Inherited = false;
            ModSettingChanged?.Invoke(collectionId, modDirectory, false);
            return 0;
        }

        public int RemoveTemporarySettings(Guid collectionId, string modDirectory, int key)
        {
            RemoveRequests.Add((collectionId, modDirectory, key));
            if (RemoveTemporaryResult != 0)
            {
                return RemoveTemporaryResult;
            }

            Temporary = null;
            ModSettingChanged?.Invoke(collectionId, modDirectory, false);
            return 0;
        }

        public void SetExternal(IReadOnlyList<string> options, string modDirectory = "FFXIVSpanish")
        {
            Current = new Dictionary<string, IReadOnlyList<string>> { [TranslationCategoryCatalog.GroupName] = options };
            ModSettingChanged?.Invoke(Default.Id, modDirectory, false);
        }

        public void SetExternalEnabled(bool enabled, string modDirectory = "FFXIVSpanish")
        {
            if (Temporary is { Exists: true } temporary)
            {
                Temporary = temporary with { Enabled = enabled };
            }
            else
            {
                Enabled = enabled;
            }
            ModSettingChanged?.Invoke(Default.Id, modDirectory, false);
        }

        public void SetForeignTemporary(string source)
            => Temporary = new PenumbraTemporaryModSettingsRead(
                0,
                true,
                false,
                true,
                Priority,
                Current,
                source);

        public void SetOwnTemporary()
            => Temporary = new PenumbraTemporaryModSettingsRead(
                0,
                true,
                false,
                true,
                Priority,
                Current,
                "FFXIV Spanish Translation Manager");

        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingLogger : IPluginDiagnosticLogger
    {
        public void Information(string message) { }
        public void Warning(string message, Exception? exception = null) { }
    }
}
