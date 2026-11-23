using FFXIVSpanishPatcher.Pipeline;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class TranslationRegenerationServiceTests
{
    [Fact]
    public async Task Regeneration_CleansObsoleteRootArtifactsAndTemporaryCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffxivspanish-regeneration-" + Guid.NewGuid().ToString("N"));
        var modRoot = Path.Combine(root, "mods");
        var candidate = Path.Combine(root, "candidate");
        Directory.CreateDirectory(Path.Combine(modRoot, ".ffxivspanish-pipeline-staging"));
        Directory.CreateDirectory(Path.Combine(modRoot, ".FFXIVSpanish.ffxivspanish-candidate-old"));
        Directory.CreateDirectory(candidate);
        try
        {
            var fingerprint = Fingerprint();
            var service = new TranslationRegenerationService(
                new FakeBuilder(new PatchResult(PatchOutcome.Ok, candidate, new PatchStatistics())),
                new FakeInstaller(new ModInstallResult(true, "ok", "FFXIVSpanish", fingerprint)),
                new Store(),
                new PluginConfiguration());

            var result = await service.RegenerateAsync(
                new TreeBuildRequest(candidate, fingerprint), modRoot, true, TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded);
            Assert.False(Directory.Exists(Path.Combine(modRoot, ".ffxivspanish-pipeline-staging")));
            Assert.False(Directory.Exists(Path.Combine(modRoot, ".FFXIVSpanish.ffxivspanish-candidate-old")));
            Assert.False(Directory.Exists(candidate));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Regeneration_RemovesWhatAnEarlierInstallationLeftBehind()
    {
        var modRoot = Path.Combine(Path.GetTempPath(), "ffxivspanish-leftovers-" + Guid.NewGuid().ToString("N"));
        string[] removed =
        [
            ".FFXIVSpanish.ffxivspanish-failed-aaaa",
            ".FFXIVSpanish.ffxivspanish-stage-bbbb",
            // Its tree is back in place, so this copy is redundant.
            ".FFXIVSpanish.ffxivspanish-backup-cccc",
            ".Mi.Mod.ffxivspanish-failed-dddd",
        ];
        string[] kept =
        [
            "FFXIVSpanish",
            // The game died mid-install: no "Perdido" tree exists, so this is the only copy.
            ".Perdido.ffxivspanish-backup-eeee",
            "OtroMod",
            ".oculto",
            // Without the leading dot it is somebody's mod, whatever it is called.
            "Mod.ffxivspanish-failed-ffff",
        ];
        foreach (var name in removed.Concat(kept))
        {
            Directory.CreateDirectory(Path.Combine(modRoot, name));
            File.WriteAllText(Path.Combine(modRoot, name, "meta.json"), "{}");
        }

        try
        {
            var fingerprint = Fingerprint();
            var service = new TranslationRegenerationService(
                new FakeBuilder(new PatchResult(PatchOutcome.Ok, "/candidate", new PatchStatistics())),
                new FakeInstaller(new ModInstallResult(true, "ok", "FFXIVSpanish", fingerprint)),
                new Store(),
                new PluginConfiguration());

            await service.RegenerateAsync(
                new TreeBuildRequest("/candidate", fingerprint), modRoot, true, TestContext.Current.CancellationToken);

            Assert.All(removed, name => Assert.False(Directory.Exists(Path.Combine(modRoot, name)), name));
            Assert.All(kept, name => Assert.True(Directory.Exists(Path.Combine(modRoot, name)), name));
        }
        finally
        {
            Directory.Delete(modRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Installation_RecordsTheOwedActivationBeforeTheInstallerRuns()
    {
        var configuration = new PluginConfiguration();
        var store = new Store();
        bool? pendingSeenByInstaller = null;
        var installer = new FakeInstaller(new ModInstallResult(false, "falló"))
        {
            OnInstall = () => pendingSeenByInstaller = store.Saved?.ActivationPending,
        };
        var service = new TranslationRegenerationService(
            new FakeBuilder(new PatchResult(PatchOutcome.Ok, "/candidate", new PatchStatistics())),
            installer,
            store,
            configuration);

        await service.RegenerateAsync(Request(), "/mods", true, TestContext.Current.CancellationToken);

        // Saved before the installer could switch the mod off, and still owed after it failed.
        Assert.True(pendingSeenByInstaller);
        Assert.True(configuration.ActivationPending);
    }

    [Fact]
    public async Task FailedBuild_DoesNotAttemptInstallationOrPersistFingerprint()
    {
        var builder = new FakeBuilder(new PatchResult(PatchOutcome.ValidationFailed, null, new PatchStatistics()));
        var installer = new FakeInstaller();
        var store = new Store();
        var configuration = new PluginConfiguration();
        var service = new TranslationRegenerationService(builder, installer, store, configuration);

        var result = await service.RegenerateAsync(Request(), "/mods", true, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(installer.WasCalled);
        Assert.Null(configuration.LastSuccessfulFingerprint);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task VerifiedBuild_InstallsAndPersistsOnlyAfterSuccess()
    {
        var fingerprint = Fingerprint();
        var builder = new FakeBuilder(new PatchResult(PatchOutcome.Ok, "/candidate", new PatchStatistics()));
        var installer = new FakeInstaller(new ModInstallResult(true, "ok", "FFXIVSpanish", fingerprint));
        var store = new Store();
        var configuration = new PluginConfiguration();
        var service = new TranslationRegenerationService(builder, installer, store, configuration);

        var result = await service.RegenerateAsync(
            new TreeBuildRequest("/candidate", fingerprint),
            "/mods",
            false,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(installer.WasCalled);
        Assert.False(installer.Request!.EnableInDefaultCollection);
        Assert.Same(fingerprint, configuration.LastSuccessfulFingerprint);
        Assert.Same(configuration, store.Saved);
    }

    private static TreeBuildRequest Request() => new("/candidate", Fingerprint());
    private static BuildFingerprint Fingerprint() => new(1, "plugin", "corpus", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);

    private sealed class FakeBuilder(PatchResult result) : ITranslationTreeBuilder
    {
        public Task<PatchResult> BuildAsync(TreeBuildRequest request, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FakeInstaller(ModInstallResult? result = null) : IModInstaller
    {
        public bool WasCalled { get; private set; }
        public ModInstallRequest? Request { get; private set; }
        public Action? OnInstall { get; init; }
        public Task<ModInstallResult> InstallAsync(ModInstallRequest request, CancellationToken cancellationToken)
        {
            OnInstall?.Invoke();
            WasCalled = true;
            Request = request;
            return Task.FromResult(result ?? new ModInstallResult(false, "not configured"));
        }
    }

    private sealed class Store : IPluginConfigurationStore
    {
        public PluginConfiguration? Saved { get; private set; }
        public bool IsReadOnly => false;
        public PluginConfiguration Load() => new();
        public void Save(PluginConfiguration configuration) => Saved = configuration;
    }
}
