namespace FFXIVSpanishPatcher.Plugin;

public enum PluginPhase
{
    Starting,
    PenumbraUnavailable,
    WelcomeRequired,
    Checking,
    UpToDate,
    Generating,
    AwaitingCompatibilityConsent,
    Installing,
    Reloading,
    Ready,
    ReadyWithOmissions,
    Cancelled,
    ErrorRecoverable,
    ErrorFatal,
    Disposed,
}

public sealed record PluginStateSnapshot(
    PluginPhase Phase,
    Guid OperationId,
    string Message,
    Exception? TechnicalError);

public interface IPluginClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemPluginClock : IPluginClock
{
    public static SystemPluginClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Owns state transitions and serializes future generation/install operations.</summary>
public sealed class TranslationManagerController : IAsyncDisposable
{
    private readonly IPluginClock _clock;
    private readonly PluginEventQueue _events;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _activeOperationCancellation;
    private bool _disposed;

    public TranslationManagerController(IPluginClock clock, PluginEventQueue? events = null)
    {
        _clock = clock;
        _events = events ?? new PluginEventQueue();
        Snapshot = new PluginStateSnapshot(PluginPhase.Starting, Guid.Empty, "Iniciando gestor.", null);
    }

    public PluginStateSnapshot Snapshot { get; private set; }

    public IReadOnlyList<PluginEvent> DrainEvents() => _events.Drain();

    public bool TryTransition(PluginPhase next, string message, Exception? technicalError = null)
    {
        lock (_stateGate)
        {
            if (_disposed || !PluginStateMachine.CanTransition(Snapshot.Phase, next))
            {
                return false;
            }

            // A check begins a new user-visible operation. Its following states share that
            // identifier so the eventual UI can group the correlated events.
            var operationId = next == PluginPhase.Checking || Snapshot.OperationId == Guid.Empty
                ? Guid.NewGuid()
                : Snapshot.OperationId;
            Snapshot = new PluginStateSnapshot(next, operationId, message, technicalError);
            _events.Enqueue(new PluginEvent(
                _clock.UtcNow,
                operationId,
                technicalError is null ? PluginEventSeverity.Information : PluginEventSeverity.Error,
                message));
            return true;
        }
    }

    public async Task<T> RunExclusiveAsync<T>(
        PluginPhase activePhase,
        Func<CancellationToken, Task<(T Value, PluginPhase completedPhase, string message)>> action,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Ya hay una operación de generación o instalación activa.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_stateGate)
        {
            _activeOperationCancellation = linkedCancellation;
        }

        try
        {
            if (!TryTransition(activePhase, "Operación iniciada."))
            {
                throw new InvalidOperationException($"No se puede iniciar {activePhase} desde {Snapshot.Phase}.");
            }

            var (value, completedPhase, message) = await action(linkedCancellation.Token).ConfigureAwait(false);
            if (!TryTransition(completedPhase, message))
            {
                throw new InvalidOperationException($"No se puede completar {activePhase} como {completedPhase}.");
            }

            return value;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            TryTransition(PluginPhase.Cancelled, "Operación cancelada.");
            throw;
        }
        catch (Exception exception)
        {
            TryTransition(PluginPhase.ErrorRecoverable, "Operación interrumpida; puede reintentarse.", exception);
            throw;
        }
        finally
        {
            lock (_stateGate)
            {
                _activeOperationCancellation = null;
            }

            _operationGate.Release();
        }
    }

    public void CancelActiveOperation()
    {
        lock (_stateGate)
        {
            _activeOperationCancellation?.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task wait;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activeOperationCancellation?.Cancel();
            wait = _operationGate.WaitAsync();
        }

        await wait.ConfigureAwait(false);
        _operationGate.Release();
        _operationGate.Dispose();
        Snapshot = Snapshot with { Phase = PluginPhase.Disposed, Message = "Gestor descargado." };
    }
}

public static class PluginStateMachine
{
    public static bool CanTransition(PluginPhase current, PluginPhase next)
        => current != PluginPhase.Disposed
            && (current, next) switch
            {
                (_, PluginPhase.Disposed) => true,
                (PluginPhase.Starting, PluginPhase.PenumbraUnavailable or PluginPhase.WelcomeRequired or PluginPhase.Checking or PluginPhase.ErrorFatal) => true,
                (PluginPhase.PenumbraUnavailable, PluginPhase.Checking or PluginPhase.Ready or PluginPhase.ErrorFatal) => true,
                (PluginPhase.WelcomeRequired, PluginPhase.Checking) => true,
                (PluginPhase.Checking, PluginPhase.UpToDate or PluginPhase.Generating or PluginPhase.AwaitingCompatibilityConsent or PluginPhase.PenumbraUnavailable or PluginPhase.ErrorRecoverable or PluginPhase.Cancelled) => true,
                (PluginPhase.AwaitingCompatibilityConsent, PluginPhase.Generating or PluginPhase.Cancelled) => true,
                (PluginPhase.Generating, PluginPhase.Installing or PluginPhase.Ready or PluginPhase.ReadyWithOmissions or PluginPhase.ErrorRecoverable or PluginPhase.Cancelled) => true,
                (PluginPhase.Installing, PluginPhase.Reloading or PluginPhase.ErrorRecoverable or PluginPhase.Cancelled) => true,
                (PluginPhase.Reloading, PluginPhase.Ready or PluginPhase.ReadyWithOmissions or PluginPhase.ErrorRecoverable or PluginPhase.Cancelled) => true,
                (PluginPhase.UpToDate or PluginPhase.Ready or PluginPhase.ReadyWithOmissions or PluginPhase.Cancelled or PluginPhase.ErrorRecoverable, PluginPhase.Checking) => true,
                _ => false,
            };
}
