using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PluginLoadProbeTests
{
    [Fact]
    public async Task LoadAsync_UsesBackgroundWorkerAndReadsExcel()
    {
        // "Background" means the caller is not kept waiting for the read, which a thread id cannot
        // prove: once the caller awaits, the pool may run the worker on that very thread. The probe
        // instead waits for the caller to get its task back; a read done inline could never see it.
        using var callerReturned = new ManualResetEventSlim();
        var probe = new RecordingProbe(readable: true, callerReturned);
        await using var loader = new PluginLoadProbe(probe);

        var loading = loader.LoadAsync(TestContext.Current.CancellationToken);
        callerReturned.Set();
        await loading;

        Assert.True(probe.WasCalled);
        Assert.True(probe.RanAfterCallerReturned);
    }

    [Fact]
    public async Task DisposeAsync_CancelsPendingWorker()
    {
        var probe = new BlockingProbe();
        var loader = new PluginLoadProbe(probe);
        var loading = loader.LoadAsync(TestContext.Current.CancellationToken);
        await probe.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        await loader.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await loading);
    }

    [Fact]
    public async Task LoadAsync_FailsWhenDalamudDataIsUnreadable()
    {
        await using var loader = new PluginLoadProbe(new RecordingProbe(readable: false));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await loader.LoadAsync(TestContext.Current.CancellationToken));
    }

    private sealed class RecordingProbe(bool readable, ManualResetEventSlim? callerReturned = null)
        : IPluginGameDataProbe
    {
        public bool WasCalled { get; private set; }
        public bool RanAfterCallerReturned { get; private set; }

        public bool TryReadKnownExcel(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RanAfterCallerReturned = callerReturned?.Wait(TimeSpan.FromSeconds(5), cancellationToken) ?? false;
            WasCalled = true;
            return readable;
        }
    }

    private sealed class BlockingProbe : IPluginGameDataProbe
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryReadKnownExcel(CancellationToken cancellationToken)
        {
            Started.SetResult(true);
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
    }
}
