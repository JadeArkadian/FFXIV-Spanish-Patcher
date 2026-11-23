using System.Collections.Concurrent;

namespace FFXIVSpanishPatcher.Plugin;

public enum PluginEventSeverity
{
    Trace,
    Information,
    Warning,
    Error,
}

public sealed record PluginEvent(
    DateTimeOffset TimestampUtc,
    Guid OperationId,
    PluginEventSeverity Severity,
    string Message);

/// <summary>Bounded concurrent event queue consumed by a future UI without replaying stale logs forever.</summary>
public sealed class PluginEventQueue
{
    private readonly int _capacity;
    private readonly ConcurrentQueue<PluginEvent> _events = new();
    private int _count;

    public PluginEventQueue(int capacity = 512)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public void Enqueue(PluginEvent value)
    {
        _events.Enqueue(value);
        var count = Interlocked.Increment(ref _count);
        while (count > _capacity && _events.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _count);
        }
    }

    public IReadOnlyList<PluginEvent> Drain()
    {
        var values = new List<PluginEvent>();
        while (_events.TryDequeue(out var value))
        {
            Interlocked.Decrement(ref _count);
            values.Add(value);
        }

        return values;
    }
}
