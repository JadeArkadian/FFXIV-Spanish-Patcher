using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class TranslationManagerControllerTests
{
    [Fact]
    public async Task RunExclusiveAsync_CompletesAndCorrelatesEventsForOneOperation()
    {
        await using var controller = new TranslationManagerController(new TestClock());

        var value = await controller.RunExclusiveAsync(
            PluginPhase.Checking,
            _ => Task.FromResult(("ready", PluginPhase.UpToDate, "Ya está actualizado.")),
            TestContext.Current.CancellationToken);

        Assert.Equal("ready", value);
        Assert.Equal(PluginPhase.UpToDate, controller.Snapshot.Phase);
        var events = controller.DrainEvents();
        Assert.Equal(2, events.Count);
        Assert.All(events, item => Assert.Equal(events[0].OperationId, item.OperationId));
    }

    [Fact]
    public async Task RunExclusiveAsync_RejectsConcurrentOperation()
    {
        await using var controller = new TranslationManagerController(new TestClock());
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = controller.RunExclusiveAsync(
            PluginPhase.Checking,
            async token =>
            {
                started.SetResult(true);
                await release.Task.WaitAsync(token);
                return (1, PluginPhase.UpToDate, "Listo.");
            },
            TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.RunExclusiveAsync(
                PluginPhase.Checking,
                _ => Task.FromResult((2, PluginPhase.UpToDate, "No debe ejecutarse.")),
                TestContext.Current.CancellationToken));

        release.SetResult(true);
        Assert.Equal(1, await first);
    }

    [Fact]
    public async Task CancelActiveOperation_TransitionsToCancelled()
    {
        await using var controller = new TranslationManagerController(new TestClock());
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = controller.RunExclusiveAsync(
            PluginPhase.Checking,
            async token =>
            {
                started.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return (0, PluginPhase.UpToDate, "No llega.");
            },
            TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        controller.CancelActiveOperation();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
        Assert.Equal(PluginPhase.Cancelled, controller.Snapshot.Phase);
    }

    [Fact]
    public async Task RunExclusiveAsync_Failure_IsRecoverableAndPreservesTechnicalError()
    {
        await using var controller = new TranslationManagerController(new TestClock());
        var failure = new InvalidOperationException("error de prueba");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.RunExclusiveAsync<int>(
                PluginPhase.Checking,
                _ => Task.FromException<(int Value, PluginPhase completedPhase, string message)>(failure),
                TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(PluginPhase.ErrorRecoverable, controller.Snapshot.Phase);
        Assert.Same(failure, controller.Snapshot.TechnicalError);
    }

    [Fact]
    public async Task DisposeAsync_CancelsActiveOperationAndDisablesFurtherTransitions()
    {
        var controller = new TranslationManagerController(new TestClock());
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = controller.RunExclusiveAsync(
            PluginPhase.Checking,
            async token =>
            {
                started.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return (0, PluginPhase.UpToDate, "No llega.");
            },
            TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        await controller.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
        Assert.Equal(PluginPhase.Disposed, controller.Snapshot.Phase);
        Assert.False(controller.TryTransition(PluginPhase.Checking, "No permitido."));
    }

    [Fact]
    public async Task TryTransition_OnlyAllowsDocumentedPathsAndStartsNewCheckCorrelation()
    {
        await using var controller = new TranslationManagerController(new TestClock());

        Assert.False(controller.TryTransition(PluginPhase.Installing, "Inválido."));
        Assert.True(controller.TryTransition(PluginPhase.WelcomeRequired, "Bienvenida."));
        Assert.True(controller.TryTransition(PluginPhase.Checking, "Comprobando."));
        var firstCheck = controller.Snapshot.OperationId;
        Assert.True(controller.TryTransition(PluginPhase.UpToDate, "Actualizado."));
        Assert.True(controller.TryTransition(PluginPhase.Checking, "Comprobando otra vez."));

        Assert.NotEqual(firstCheck, controller.Snapshot.OperationId);
    }

    [Fact]
    public async Task RunExclusiveAsync_GenerationMayCompleteReadyAfterVerifiedInstall()
    {
        await using var controller = new TranslationManagerController(new TestClock());
        Assert.True(controller.TryTransition(PluginPhase.WelcomeRequired, "Primera instalación."));
        Assert.True(controller.TryTransition(PluginPhase.Checking, "Preparando."));

        var result = await controller.RunExclusiveAsync(
            PluginPhase.Generating,
            _ => Task.FromResult((true, PluginPhase.Ready, "Traducción instalada en Penumbra.")),
            TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(PluginPhase.Ready, controller.Snapshot.Phase);
    }

    private sealed class TestClock : IPluginClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-08-21T00:00:00Z");
    }
}
