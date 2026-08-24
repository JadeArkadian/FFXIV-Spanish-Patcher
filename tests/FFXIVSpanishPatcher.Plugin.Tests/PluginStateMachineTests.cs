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
    [InlineData(PluginPhase.Starting, PluginPhase.Installing)]
    [InlineData(PluginPhase.PenumbraUnavailable, PluginPhase.Generating)]
    [InlineData(PluginPhase.WelcomeRequired, PluginPhase.Installing)]
    [InlineData(PluginPhase.Checking, PluginPhase.Ready)]
    [InlineData(PluginPhase.Generating, PluginPhase.Reloading)]
    [InlineData(PluginPhase.Installing, PluginPhase.Ready)]
    [InlineData(PluginPhase.Reloading, PluginPhase.Generating)]
    [InlineData(PluginPhase.UpToDate, PluginPhase.Ready)]
    [InlineData(PluginPhase.Disposed, PluginPhase.Checking)]
    public void CanTransition_SkippedOrTerminalTransition_IsRejected(PluginPhase current, PluginPhase next)
        => Assert.False(PluginStateMachine.CanTransition(current, next));

    public static IEnumerable<object[]> AllowedTransitions()
    {
        yield return [PluginPhase.Starting, PluginPhase.PenumbraUnavailable];
        yield return [PluginPhase.Starting, PluginPhase.WelcomeRequired];
        yield return [PluginPhase.Starting, PluginPhase.Checking];
        yield return [PluginPhase.Starting, PluginPhase.ErrorFatal];
        yield return [PluginPhase.PenumbraUnavailable, PluginPhase.Checking];
        yield return [PluginPhase.PenumbraUnavailable, PluginPhase.Ready];
        yield return [PluginPhase.PenumbraUnavailable, PluginPhase.ErrorFatal];
        yield return [PluginPhase.WelcomeRequired, PluginPhase.Checking];
        yield return [PluginPhase.Checking, PluginPhase.UpToDate];
        yield return [PluginPhase.Checking, PluginPhase.Generating];
        yield return [PluginPhase.Checking, PluginPhase.AwaitingCompatibilityConsent];
        yield return [PluginPhase.Checking, PluginPhase.PenumbraUnavailable];
        yield return [PluginPhase.Checking, PluginPhase.ErrorRecoverable];
        yield return [PluginPhase.Checking, PluginPhase.Cancelled];
        yield return [PluginPhase.AwaitingCompatibilityConsent, PluginPhase.Generating];
        yield return [PluginPhase.AwaitingCompatibilityConsent, PluginPhase.Cancelled];
        yield return [PluginPhase.Generating, PluginPhase.Installing];
        yield return [PluginPhase.Generating, PluginPhase.ReadyWithOmissions];
        yield return [PluginPhase.Generating, PluginPhase.ErrorRecoverable];
        yield return [PluginPhase.Generating, PluginPhase.Cancelled];
        yield return [PluginPhase.Installing, PluginPhase.Reloading];
        yield return [PluginPhase.Installing, PluginPhase.ErrorRecoverable];
        yield return [PluginPhase.Installing, PluginPhase.Cancelled];
        yield return [PluginPhase.Reloading, PluginPhase.Ready];
        yield return [PluginPhase.Reloading, PluginPhase.ReadyWithOmissions];
        yield return [PluginPhase.Reloading, PluginPhase.ErrorRecoverable];
        yield return [PluginPhase.Reloading, PluginPhase.Cancelled];
        yield return [PluginPhase.UpToDate, PluginPhase.Checking];
        yield return [PluginPhase.Ready, PluginPhase.Checking];
        yield return [PluginPhase.ReadyWithOmissions, PluginPhase.Checking];
        yield return [PluginPhase.Cancelled, PluginPhase.Checking];
        yield return [PluginPhase.ErrorRecoverable, PluginPhase.Checking];

        foreach (var state in Enum.GetValues<PluginPhase>().Where(state => state != PluginPhase.Disposed))
        {
            yield return [state, PluginPhase.Disposed];
        }
    }
}
