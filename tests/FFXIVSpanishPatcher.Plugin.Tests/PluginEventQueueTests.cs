using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PluginEventQueueTests
{
    [Fact]
    public void Enqueue_WhenCapacityIsExceeded_DropsOldestEvents()
    {
        var queue = new PluginEventQueue(capacity: 2);
        queue.Enqueue(Event("uno"));
        queue.Enqueue(Event("dos"));
        queue.Enqueue(Event("tres"));

        var values = queue.Drain();

        Assert.Equal(["dos", "tres"], values.Select(item => item.Message));
        Assert.Empty(queue.Drain());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidCapacity_Throws(int capacity)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PluginEventQueue(capacity));

    private static PluginEvent Event(string message)
        => new(DateTimeOffset.UnixEpoch, Guid.Empty, PluginEventSeverity.Information, message);
}
