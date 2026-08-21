using Dalamud.Plugin.Ipc.Exceptions;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PenumbraIpcClientTests
{
    [Fact]
    public async Task GetAvailabilityAsync_ReadyPenumbra_ReturnsTypedReadyState()
    {
        var transport = new FakeTransport();
        var probe = new FakeDirectoryProbe(existing: true);
        var log = new RecordingLogger();
        await using var client = new PenumbraIpcClient(transport, probe, log);

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.True(availability.IsReady);
        Assert.Equal(PenumbraAvailabilityStatus.Ready, availability.Status);
        Assert.Equal(5, availability.BreakingVersion);
        Assert.Equal(17, availability.FeatureVersion);
        Assert.Equal("/mods", availability.ModDirectory);
    }

    [Fact]
    public async Task GetAvailabilityAsync_NotInstalledOrLateLoading_IsRecoverable()
    {
        var transport = new FakeTransport { ApiVersionException = new IpcNotReadyError("Penumbra.ApiVersion.V5") };
        var log = new RecordingLogger();
        await using var client = new PenumbraIpcClient(transport, new FakeDirectoryProbe(existing: true), log);

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PenumbraAvailabilityStatus.Unavailable, availability.Status);
        Assert.IsType<IpcNotReadyError>(availability.TechnicalError);
        Assert.Single(log.InformationMessages);
    }

    [Fact]
    public async Task GetAvailabilityAsync_DisabledPenumbra_ReturnsDisabledState()
    {
        var transport = new FakeTransport { IsEnabled = false };
        await using var client = CreateClient(transport);

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PenumbraAvailabilityStatus.Disabled, availability.Status);
    }

    [Theory]
    [InlineData(4, 17, PenumbraAvailabilityStatus.IncompatibleBreakingVersion)]
    [InlineData(5, 14, PenumbraAvailabilityStatus.InsufficientFeatureVersion)]
    public async Task GetAvailabilityAsync_IncompatibleApi_ReturnsTypedState(int breaking, int features, PenumbraAvailabilityStatus expected)
    {
        var transport = new FakeTransport { Version = (breaking, features) };
        await using var client = CreateClient(transport);

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, availability.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/missing")]
    public async Task GetAvailabilityAsync_EmptyOrInvalidModDirectory_ReturnsTypedState(string directory)
    {
        var transport = new FakeTransport { ModDirectory = directory };
        await using var client = new PenumbraIpcClient(transport, new FakeDirectoryProbe(existing: false), new RecordingLogger());

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PenumbraAvailabilityStatus.InvalidModDirectory, availability.Status);
    }

    [Theory]
    [InlineData(Wrapper.ApiVersion)]
    [InlineData(Wrapper.EnabledState)]
    [InlineData(Wrapper.ModDirectory)]
    public async Task GetAvailabilityAsync_WrapperException_BecomesFaultedDiagnostic(Wrapper wrapper)
    {
        var failure = new InvalidOperationException(wrapper.ToString());
        var transport = new FakeTransport();
        transport.SetException(wrapper, failure);
        var log = new RecordingLogger();
        await using var client = new PenumbraIpcClient(transport, new FakeDirectoryProbe(existing: true), log);

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PenumbraAvailabilityStatus.Faulted, availability.Status);
        Assert.Same(failure, availability.TechnicalError);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public async Task InitializedAfterLateLoad_RechecksAndPublishesReadyState()
    {
        var transport = new FakeTransport { ApiVersionException = new IpcNotReadyError("Penumbra.ApiVersion.V5") };
        await using var client = CreateClient(transport);
        var observed = new List<PenumbraAvailability>();
        client.AvailabilityChanged += observed.Add;

        transport.ApiVersionException = null;
        transport.RaiseInitialized();

        var availability = Assert.Single(observed);
        Assert.True(availability.IsReady);
    }

    [Fact]
    public async Task DisposedThenInitialized_PublishesUnavailableThenReconnects()
    {
        var transport = new FakeTransport();
        await using var client = CreateClient(transport);
        var observed = new List<PenumbraAvailability>();
        client.AvailabilityChanged += observed.Add;

        transport.RaiseDisposed();
        transport.RaiseInitialized();

        Assert.Equal(
            [PenumbraAvailabilityStatus.Unavailable, PenumbraAvailabilityStatus.Ready],
            observed.Select(item => item.Status));
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesEveryTransportEvent()
    {
        var transport = new FakeTransport();
        var client = CreateClient(transport);
        var observed = new List<PenumbraAvailability>();
        client.AvailabilityChanged += observed.Add;

        await client.DisposeAsync();
        transport.RaiseInitialized();
        transport.RaiseDisposed();
        transport.RaiseEnabledChanged();
        transport.RaiseModDirectoryChanged();

        Assert.True(transport.IsDisposed);
        Assert.Empty(observed);
    }

    private static PenumbraIpcClient CreateClient(FakeTransport transport)
        => new(transport, new FakeDirectoryProbe(existing: true), new RecordingLogger());

    public enum Wrapper
    {
        ApiVersion,
        EnabledState,
        ModDirectory,
    }

    private sealed class FakeTransport : IPenumbraApiTransport
    {
        public (int Breaking, int Features) Version { get; set; } = (5, 17);
        public bool IsEnabled { get; set; } = true;
        public string ModDirectory { get; set; } = "/mods";
        public Exception? ApiVersionException { get; set; }
        public Exception? EnabledStateException { get; set; }
        public Exception? ModDirectoryException { get; set; }
        public bool IsDisposed { get; private set; }

        public event Action? Initialized;
        public event Action? Disposed;
        public event Action<bool>? EnabledChanged;
        public event Action<string, bool>? ModDirectoryChanged;

        public (int Breaking, int Features) GetApiVersion()
        {
            if (ApiVersionException is not null)
            {
                throw ApiVersionException;
            }

            return Version;
        }

        public bool GetEnabledState()
        {
            if (EnabledStateException is not null)
            {
                throw EnabledStateException;
            }

            return IsEnabled;
        }

        public string GetModDirectory()
        {
            if (ModDirectoryException is not null)
            {
                throw ModDirectoryException;
            }

            return ModDirectory;
        }

        public void SetException(Wrapper wrapper, Exception exception)
        {
            switch (wrapper)
            {
                case Wrapper.ApiVersion:
                    ApiVersionException = exception;
                    break;
                case Wrapper.EnabledState:
                    EnabledStateException = exception;
                    break;
                case Wrapper.ModDirectory:
                    ModDirectoryException = exception;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(wrapper));
            }
        }

        public void RaiseInitialized() => Initialized?.Invoke();
        public void RaiseDisposed() => Disposed?.Invoke();
        public void RaiseEnabledChanged() => EnabledChanged?.Invoke(IsEnabled);
        public void RaiseModDirectoryChanged() => ModDirectoryChanged?.Invoke(ModDirectory, true);
        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeDirectoryProbe(bool existing) : IPenumbraDirectoryProbe
    {
        public bool Exists(string path) => existing;
    }

    private sealed class RecordingLogger : IPluginDiagnosticLogger
    {
        public List<string> InformationMessages { get; } = [];
        public List<(string Message, Exception? Exception)> Warnings { get; } = [];

        public void Information(string message) => InformationMessages.Add(message);
        public void Warning(string message, Exception? exception = null) => Warnings.Add((message, exception));
    }
}
