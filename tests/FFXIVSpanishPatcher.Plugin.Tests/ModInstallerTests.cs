using System.Text.Json;
using FFXIVSpanishPatcher.Pipeline;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class ModInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ffxivspanish-installer-" + Guid.NewGuid().ToString("N"));
    private readonly string _modRoot;
    private readonly string _candidate;

    public ModInstallerTests()
    {
        _modRoot = Path.Combine(_root, "mods");
        _candidate = Path.Combine(_root, "candidate");
        Directory.CreateDirectory(_modRoot);
        WriteV4Tree(_candidate);
    }

    [Fact]
    public async Task FirstInstall_PromotesVerifiedTreeAndRegistersIt()
    {
        var registry = new FakeRegistry();
        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.AdoptedExistingMod);
        Assert.Equal(PenumbraModIdentity.CanonicalDirectory, result.ModDirectory);
        Assert.Equal([PenumbraModIdentity.CanonicalDirectory], registry.Added);
        Assert.Equal([(PenumbraModIdentity.CanonicalDirectory, true)], registry.Enabled);
        Assert.Empty(registry.Reloaded);
        Assert.Equal(4, ReadFileVersion(Path.Combine(_modRoot, PenumbraModIdentity.CanonicalDirectory)));
        Assert.Equal(Request().Fingerprint, result.PersistedFingerprint);
    }

    [Fact]
    public async Task Update_DoesNotAdoptV040PmpWithoutStableIdentity()
    {
        var existing = Path.Combine(_modRoot, "SpanishV040");
        WriteMetadata(existing, 3, managedTag: false, mutatePresentation: true);
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["SpanishV040"] = PenumbraModIdentity.LogicalName } };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.AdoptedExistingMod);
        Assert.Equal([PenumbraModIdentity.CanonicalDirectory], registry.Added);
        Assert.Empty(registry.Reloaded);
        Assert.True(Directory.Exists(existing));
        Assert.Equal(3, ReadFileVersion(existing));
    }

    [Fact]
    public Task Update_AdoptsV050PmpWithoutManagedTag_PreservesDirectoryAndReloads()
        => AssertAdoptsPriorPmpAsync(fileVersion: 4, managedTag: false, mutatePresentation: true);

    [Fact]
    public Task Update_AdoptsAlreadyManagedV4Tree_PreservesDirectoryAndReloads()
        => AssertAdoptsPriorPmpAsync(fileVersion: 4, managedTag: true);

    private async Task AssertAdoptsPriorPmpAsync(int fileVersion, bool managedTag, bool mutatePresentation = false)
    {
        var existing = Path.Combine(_modRoot, "SpanishLegacy");
        WriteMetadata(existing, fileVersion, managedTag, mutatePresentation);
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName } };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.AdoptedExistingMod);
        Assert.Equal("SpanishLegacy", result.ModDirectory);
        Assert.Empty(registry.Added);
        Assert.Equal([("SpanishLegacy", true)], registry.Enabled);
        Assert.Equal(["SpanishLegacy"], registry.Reloaded);
        Assert.Equal(4, ReadFileVersion(existing));
        Assert.Empty(Directory.EnumerateDirectories(_modRoot, ".SpanishLegacy.ffxivspanish-backup-*"));
    }

    [Fact]
    public async Task Update_DoesNotAdoptUnrecognizedV4ModEvenWhenPenumbraListsItWithTheSameName()
    {
        var existing = Path.Combine(_modRoot, "OtherMod");
        WriteMetadata(existing, 4, managedTag: false, includeLegacyFingerprint: false);
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["OtherMod"] = PenumbraModIdentity.LogicalName } };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.AdoptedExistingMod);
        Assert.Equal([PenumbraModIdentity.CanonicalDirectory], registry.Added);
        Assert.Empty(registry.Reloaded);
        Assert.True(Directory.Exists(existing));
    }

    [Fact]
    public async Task Update_AdoptsValidMarkerEvenWhenAllPresentationMetadataChanges()
    {
        var existing = Path.Combine(_modRoot, "LocalName");
        WriteMetadata(existing, 4, managedTag: false, mutatePresentation: true, includeLegacyFingerprint: false);
        TranslationModIdentity.Write(existing);
        using (var marker = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(existing, TranslationModIdentity.MarkerFileName))))
        {
            Assert.Equal(TranslationModIdentity.SchemaVersion, marker.RootElement.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(TranslationModIdentity.Fingerprint, marker.RootElement.GetProperty("Fingerprint").GetString());
        }
        Assert.True(TranslationModIdentity.HasValidMarker(existing));
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["LocalName"] = "Otro nombre" } };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.AdoptedExistingMod);
        Assert.Equal("LocalName", result.ModDirectory);
        Assert.Equal([("LocalName", true)], registry.Enabled);
        Assert.Equal(["LocalName"], registry.Reloaded);
    }

    [Fact]
    public async Task DuplicateCandidates_AreConsolidatedIntoOneRegeneratedTree()
    {
        WriteMetadata(Path.Combine(_modRoot, "SpanishA"), 4, managedTag: true);
        WriteMetadata(Path.Combine(_modRoot, "SpanishB"), 4, managedTag: true);
        var registry = new FakeRegistry
        {
            Mods = new Dictionary<string, string>
            {
                ["SpanishA"] = PenumbraModIdentity.LogicalName,
                ["SpanishB"] = PenumbraModIdentity.LogicalName,
            },
        };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("1 copia", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(registry.Added);
        Assert.Equal([("SpanishA", true)], registry.Enabled);
        Assert.Equal(["SpanishB", "SpanishA"], registry.Reloaded);
        Assert.Equal("SpanishA", result.ModDirectory);
        Assert.True(Directory.Exists(Path.Combine(_modRoot, "SpanishA")));
        Assert.False(Directory.Exists(Path.Combine(_modRoot, "SpanishB")));
        Assert.Empty(Directory.EnumerateDirectories(_modRoot, ".Spanish*.ffxivspanish-backup-*"));
    }

    [Fact]
    public async Task DuplicateConsolidation_ReloadFailureRestoresEveryOriginalTree()
    {
        WriteMetadata(Path.Combine(_modRoot, "SpanishA"), 4, managedTag: true);
        WriteMetadata(Path.Combine(_modRoot, "SpanishB"), 4, managedTag: true);
        var registry = new FakeRegistry
        {
            ReloadResult = 7,
            Mods = new Dictionary<string, string>
            {
                ["SpanishA"] = PenumbraModIdentity.LogicalName,
                ["SpanishB"] = PenumbraModIdentity.LogicalName,
            },
        };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(Directory.Exists(Path.Combine(_modRoot, "SpanishA")));
        Assert.True(Directory.Exists(Path.Combine(_modRoot, "SpanishB")));
        Assert.Single(Directory.EnumerateDirectories(_modRoot, ".SpanishA.ffxivspanish-failed-*"));
    }

    [Fact]
    public async Task UnsafeIpcPath_IsIgnoredAndCannotEscapeModRoot()
    {
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["../outside"] = PenumbraModIdentity.LogicalName } };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(_root, "outside")));
        Assert.True(Directory.Exists(Path.Combine(_modRoot, PenumbraModIdentity.CanonicalDirectory)));
    }

    [Fact]
    public async Task CandidateSymlink_IsRejectedBeforeAnythingIsPromoted()
    {
        try
        {
            File.CreateSymbolicLink(Path.Combine(_candidate, "outside-link"), Path.Combine(_root, "outside"));
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await CreateInstaller(new FakeRegistry()).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("enlazado", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_modRoot, PenumbraModIdentity.CanonicalDirectory)));
    }

    [Fact]
    public async Task AddModFailure_RetainsCandidateForDiagnosticsWithoutCallingDelete()
    {
        var registry = new FakeRegistry { AddResult = 2 };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([PenumbraModIdentity.CanonicalDirectory], registry.Added);
        Assert.Empty(registry.Reloaded);
        Assert.False(Directory.Exists(Path.Combine(_modRoot, PenumbraModIdentity.CanonicalDirectory)));
        Assert.Single(Directory.EnumerateDirectories(_modRoot, ".FFXIVSpanish.ffxivspanish-failed-*"));
    }

    [Fact]
    public async Task EnableFailure_RetainsCandidateAndDoesNotPersistAUsableInstallation()
    {
        var registry = new FakeRegistry { EnableResult = 8 };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([PenumbraModIdentity.CanonicalDirectory], registry.Added);
        Assert.Equal([(PenumbraModIdentity.CanonicalDirectory, true)], registry.Enabled);
        Assert.False(Directory.Exists(Path.Combine(_modRoot, PenumbraModIdentity.CanonicalDirectory)));
        Assert.Single(Directory.EnumerateDirectories(_modRoot, ".FFXIVSpanish.ffxivspanish-failed-*"));
    }

    [Fact]
    public async Task UpdateEnableFailure_RestoresThePreviouslyDisabledTree()
    {
        var existing = Path.Combine(_modRoot, "SpanishLegacy");
        WriteMetadata(existing, 4, managedTag: true);
        var registry = new FakeRegistry
        {
            EnableResult = 8,
            Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName },
        };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([("SpanishLegacy", true)], registry.Enabled);
        Assert.Equal(4, ReadFileVersion(existing));
        Assert.Equal(["SpanishLegacy", "SpanishLegacy"], registry.Reloaded);
        Assert.Single(Directory.EnumerateDirectories(_modRoot, ".SpanishLegacy.ffxivspanish-failed-*"));
    }

    [Fact]
    public async Task EnableNothingChanged_AcceptsAlreadyEnabledMod()
    {
        var registry = new FakeRegistry { EnableResult = 1 };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Request().Fingerprint, result.PersistedFingerprint);
        Assert.Equal([(PenumbraModIdentity.CanonicalDirectory, true)], registry.Enabled);
    }

    [Fact]
    public async Task EnableIpcException_RollsBackPromotedFirstInstallation()
    {
        var registry = new FakeRegistry { EnableException = new ArgumentException("IPC type mismatch") };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(_modRoot, PenumbraModIdentity.CanonicalDirectory)));
        Assert.Single(Directory.EnumerateDirectories(_modRoot, ".FFXIVSpanish.ffxivspanish-failed-*"));
    }

    [Fact]
    public async Task ReloadFailure_RestoresPreviousVersionAndRetainsFailedCandidate()
    {
        var existing = Path.Combine(_modRoot, "SpanishLegacy");
        WriteMetadata(existing, 4, managedTag: true);
        var registry = new FakeRegistry
        {
            ReloadResult = 7,
            Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName },
        };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(4, ReadFileVersion(existing));
        Assert.Equal(["SpanishLegacy", "SpanishLegacy"], registry.Reloaded);
        Assert.Single(Directory.EnumerateDirectories(_modRoot, ".SpanishLegacy.ffxivspanish-failed-*"));
    }

    [Theory]
    [InlineData(ModInstallationStep.CandidateCopiedToStaging)]
    [InlineData(ModInstallationStep.ExistingModBackedUp)]
    [InlineData(ModInstallationStep.CandidatePromoted)]
    [InlineData(ModInstallationStep.PenumbraReloaded)]
    [InlineData(ModInstallationStep.PreviousVersionCleaned)]
    public async Task FailureAtEveryTransactionalStep_RestoresPriorTree(ModInstallationStep failedStep)
    {
        var existing = Path.Combine(_modRoot, "SpanishLegacy");
        WriteMetadata(existing, 4, managedTag: true);
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName } };
        var installer = CreateInstaller(registry, new ThrowAtStep(failedStep));

        var result = await installer.InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(failedStep == ModInstallationStep.PreviousVersionCleaned, result.Succeeded);
        Assert.Equal(4, ReadFileVersion(existing));
        Assert.True(Directory.Exists(existing));
    }

    [Fact]
    public async Task CancellationDuringInstallation_RestoresPriorTree()
    {
        var existing = Path.Combine(_modRoot, "SpanishLegacy");
        WriteMetadata(existing, 4, managedTag: true);
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName } };
        using var cancellation = new CancellationTokenSource();
        var installer = CreateInstaller(registry, new CancelAtStep(ModInstallationStep.CandidatePromoted, cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(Request(), cancellation.Token));

        Assert.Equal(4, ReadFileVersion(existing));
    }

    [Fact]
    public void RegistryContract_HasNoDeleteOperation()
        => Assert.Null(typeof(IPenumbraModRegistry).GetMethod("DeleteMod"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ModInstaller CreateInstaller(FakeRegistry registry, IModInstallationFaultInjector? faultInjector = null)
        => new(registry, new RecordingLogger(), faultInjector);

    private ModInstallRequest Request()
        => new(_candidate, _modRoot, new BuildFingerprint(1, "0.6.0", "corpus", "game", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch));

    private static void WriteV4Tree(string path)
    {
        Directory.CreateDirectory(path);
        var options = TranslationCategoryCatalog.All
            .Select(category => new { Name = category.DisplayName, Description = "", Files = new Dictionary<string, string>() })
            .ToArray();
        File.WriteAllText(Path.Combine(path, "meta.json"), JsonSerializer.Serialize(new
        {
            FileVersion = 4,
            Name = PenumbraModIdentity.LogicalName,
            Website = PenumbraModIdentity.Website,
            ModTags = new[] { PenumbraModIdentity.ManagedTag },
            Groups = new[] { new { Name = TranslationCategoryCatalog.GroupName, Type = "Multi", DefaultSettings = 0, Options = options } },
        }));
    }

    private static void WriteMetadata(
        string path,
        int fileVersion,
        bool managedTag,
        bool mutatePresentation = false,
        bool includeLegacyFingerprint = true)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "meta.json"), JsonSerializer.Serialize(new
        {
            FileVersion = fileVersion,
            Name = mutatePresentation ? "Mi traducción personalizada" : PenumbraModIdentity.LogicalName,
            Website = mutatePresentation ? "https://ejemplo.invalid/mod-local" : PenumbraModIdentity.Website,
            ModTags = managedTag ? new[] { PenumbraModIdentity.ManagedTag } : Array.Empty<string>(),
            Groups = fileVersion == 4 && includeLegacyFingerprint ? LegacyV050Groups() : null,
        }));

        if (fileVersion == 3 && includeLegacyFingerprint)
        {
            var files = Enumerable.Range(0, 25).ToDictionary(
                index => $"exd/LegacySheet_{index:D2}.exd",
                index => $"files/exd/LegacySheet_{index:D2}.exd",
                StringComparer.Ordinal);
            File.WriteAllText(Path.Combine(path, "default_mod.json"), JsonSerializer.Serialize(new { Version = 0, Files = files }));
        }
    }

    private static object[] LegacyV050Groups()
        =>
        [
            new
            {
                Name = TranslationCategoryCatalog.GroupName,
                Type = "Multi",
                Options = TranslationCategoryCatalog.All.Select(category => new
                {
                    Name = category.DisplayName,
                    Files = Enumerable.Range(0, 3).ToDictionary(
                        index => $"exd/{category.Domain}_{index:D2}.exd",
                        index => $"files/categories/{category.Order:D2}-{category.Domain}/exd/{category.Domain}_{index:D2}.exd",
                        StringComparer.Ordinal),
                }),
            },
        ];

    private static int ReadFileVersion(string path)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "meta.json"))).RootElement.GetProperty("FileVersion").GetInt32();

    private sealed class FakeRegistry : IPenumbraModRegistry
    {
        public IReadOnlyDictionary<string, string> Mods { get; set; } = new Dictionary<string, string>();
        public List<string> Added { get; } = [];
        public List<string> Reloaded { get; } = [];
        public List<(string DirectoryName, bool Enabled)> Enabled { get; } = [];
        public int AddResult { get; set; }
        public int ReloadResult { get; set; }
        public int EnableResult { get; set; }
        public Exception? EnableException { get; set; }

        public IReadOnlyDictionary<string, string> GetModList() => Mods;
        public int AddMod(string directoryName) { Added.Add(directoryName); return AddResult; }
        public int ReloadMod(string directoryName) { Reloaded.Add(directoryName); return ReloadResult; }
        public int SetEnabled(string directoryName, bool enabled)
        {
            Enabled.Add((directoryName, enabled));
            if (EnableException is not null)
            {
                throw EnableException;
            }

            return EnableResult;
        }
    }

    private sealed class RecordingLogger : IPluginDiagnosticLogger
    {
        public void Information(string message) { }
        public void Warning(string message, Exception? exception = null) { }
    }

    private sealed class ThrowAtStep(ModInstallationStep step) : IModInstallationFaultInjector
    {
        public void Before(ModInstallationStep current)
        {
            if (current == step)
            {
                throw new IOException($"simulated {step}");
            }
        }
    }

    private sealed class CancelAtStep(ModInstallationStep step, CancellationTokenSource cancellation) : IModInstallationFaultInjector
    {
        public void Before(ModInstallationStep current)
        {
            if (current == step)
            {
                cancellation.Cancel();
            }
        }
    }
}
