using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PluginStateMachineTests
{
    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void CanTransition_DocumentedTransition_IsAllowed(PluginPhase current, PluginPhase next)
        => Assert.True(PluginStateMachine.CanTransition(current, next));

    [Theory]
    [InlineData(PluginPhase.Starting, PluginPhase.Generating)]
    [InlineData(PluginPhase.PenumbraUnavailable, PluginPhase.Generating)]
    [InlineData(PluginPhase.WelcomeRequired, PluginPhase.Generating)]
    [InlineData(PluginPhase.Checking, PluginPhase.Ready)]
    [InlineData(PluginPhase.Generating, PluginPhase.Checking)]
    [InlineData(PluginPhase.Ready, PluginPhase.Generating)]
    [InlineData(PluginPhase.Cancelled, PluginPhase.Ready)]
    [InlineData(PluginPhase.Disposed, PluginPhase.Checking)]
    public void CanTransition_SkippedOrTerminalTransition_IsRejected(PluginPhase current, PluginPhase next)
        => Assert.False(PluginStateMachine.CanTransition(current, next));

    public static IEnumerable<object[]> AllowedTransitions()
    {
        yield return [PluginPhase.Starting, PluginPhase.PenumbraUnavailable];
        yield return [PluginPhase.Starting, PluginPhase.WelcomeRequired];
        yield return [PluginPhase.Starting, PluginPhase.Checking];
        yield return [PluginPhase.PenumbraUnavailable, PluginPhase.Checking];
        yield return [PluginPhase.PenumbraUnavailable, PluginPhase.Ready];
        yield return [PluginPhase.WelcomeRequired, PluginPhase.Checking];
        yield return [PluginPhase.Checking, PluginPhase.Generating];
        yield return [PluginPhase.Checking, PluginPhase.PenumbraUnavailable];
        yield return [PluginPhase.Checking, PluginPhase.ErrorRecoverable];
        yield return [PluginPhase.Checking, PluginPhase.Cancelled];
        yield return [PluginPhase.Generating, PluginPhase.Ready];
        yield return [PluginPhase.Generating, PluginPhase.ErrorRecoverable];
        yield return [PluginPhase.Generating, PluginPhase.Cancelled];
        yield return [PluginPhase.Ready, PluginPhase.Checking];
        yield return [PluginPhase.Cancelled, PluginPhase.Checking];
        yield return [PluginPhase.ErrorRecoverable, PluginPhase.Checking];

        foreach (var state in Enum.GetValues<PluginPhase>().Where(state => state != PluginPhase.Disposed))
        {
            yield return [state, PluginPhase.Disposed];
        }
    }
}
