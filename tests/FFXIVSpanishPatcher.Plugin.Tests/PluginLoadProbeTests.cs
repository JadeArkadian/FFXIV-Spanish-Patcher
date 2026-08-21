using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PluginLoadProbeTests
{
    [Fact]
    public async Task LoadAsync_UsesBackgroundWorkerAndReadsExcel()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var probe = new RecordingProbe(readable: true);
        await using var loader = new PluginLoadProbe(probe);

        await loader.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(probe.WasCalled);
        Assert.NotEqual(callerThread, probe.ThreadId);
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

    private sealed class RecordingProbe(bool readable) : IPluginGameDataProbe
    {
        public int ThreadId { get; private set; }
        public bool WasCalled { get; private set; }

        public bool TryReadKnownExcel(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThreadId = Environment.CurrentManagedThreadId;
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
