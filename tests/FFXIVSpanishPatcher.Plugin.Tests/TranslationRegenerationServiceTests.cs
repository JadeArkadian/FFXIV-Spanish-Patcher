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
        public Task<ModInstallResult> InstallAsync(ModInstallRequest request, CancellationToken cancellationToken)
        {
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
