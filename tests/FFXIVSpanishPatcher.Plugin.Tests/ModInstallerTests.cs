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
        Assert.Empty(registry.Reloaded);
        Assert.Equal(4, ReadFileVersion(Path.Combine(_modRoot, PenumbraModIdentity.CanonicalDirectory)));
        Assert.Equal(Request().Fingerprint, result.PersistedFingerprint);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task Update_AdoptsLegacyOrManagedTree_PreservesDirectoryAndReloads(int version, bool managedTag)
    {
        var existing = Path.Combine(_modRoot, "SpanishLegacy");
        WriteMetadata(existing, version, managedTag);
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName } };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.AdoptedExistingMod);
        Assert.Equal("SpanishLegacy", result.ModDirectory);
        Assert.Empty(registry.Added);
        Assert.Equal(["SpanishLegacy"], registry.Reloaded);
        Assert.Equal(4, ReadFileVersion(existing));
        Assert.Empty(Directory.EnumerateDirectories(_modRoot, ".SpanishLegacy.ffxivspanish-backup-*"));
    }

    [Fact]
    public async Task AmbiguousCandidates_RequiresExplicitSelectionAndDoesNotTouchEitherTree()
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

        Assert.False(result.Succeeded);
        Assert.Contains("varias instalaciones", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(registry.Added);
        Assert.Empty(registry.Reloaded);
        Assert.True(Directory.Exists(Path.Combine(_modRoot, "SpanishA")));
        Assert.True(Directory.Exists(Path.Combine(_modRoot, "SpanishB")));
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
    public async Task ReloadFailure_RestoresPreviousVersionAndRetainsFailedCandidate()
    {
        var existing = Path.Combine(_modRoot, "SpanishLegacy");
        WriteMetadata(existing, 3, managedTag: false);
        var registry = new FakeRegistry
        {
            ReloadResult = 7,
            Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName },
        };

        var result = await CreateInstaller(registry).InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(3, ReadFileVersion(existing));
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
        WriteMetadata(existing, 3, managedTag: false);
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName } };
        var installer = CreateInstaller(registry, new ThrowAtStep(failedStep));

        var result = await installer.InstallAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(failedStep == ModInstallationStep.PreviousVersionCleaned, result.Succeeded);
        Assert.Equal(failedStep == ModInstallationStep.PreviousVersionCleaned ? 4 : 3, ReadFileVersion(existing));
        Assert.True(Directory.Exists(existing));
    }

    [Fact]
    public async Task CancellationDuringInstallation_RestoresPriorTree()
    {
        var existing = Path.Combine(_modRoot, "SpanishLegacy");
        WriteMetadata(existing, 3, managedTag: false);
        var registry = new FakeRegistry { Mods = new Dictionary<string, string> { ["SpanishLegacy"] = PenumbraModIdentity.LogicalName } };
        using var cancellation = new CancellationTokenSource();
        var installer = CreateInstaller(registry, new CancelAtStep(ModInstallationStep.CandidatePromoted, cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(Request(), cancellation.Token));

        Assert.Equal(3, ReadFileVersion(existing));
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

    private static void WriteMetadata(string path, int fileVersion, bool managedTag)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "meta.json"), JsonSerializer.Serialize(new
        {
            FileVersion = fileVersion,
            Name = PenumbraModIdentity.LogicalName,
            Website = PenumbraModIdentity.Website,
            ModTags = managedTag ? new[] { PenumbraModIdentity.ManagedTag } : Array.Empty<string>(),
        }));
    }

    private static int ReadFileVersion(string path)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "meta.json"))).RootElement.GetProperty("FileVersion").GetInt32();

    private sealed class FakeRegistry : IPenumbraModRegistry
    {
        public IReadOnlyDictionary<string, string> Mods { get; set; } = new Dictionary<string, string>();
        public List<string> Added { get; } = [];
        public List<string> Reloaded { get; } = [];
        public int AddResult { get; set; }
        public int ReloadResult { get; set; }

        public IReadOnlyDictionary<string, string> GetModList() => Mods;
        public int AddMod(string directoryName) { Added.Add(directoryName); return AddResult; }
        public int ReloadMod(string directoryName) { Reloaded.Add(directoryName); return ReloadResult; }
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
