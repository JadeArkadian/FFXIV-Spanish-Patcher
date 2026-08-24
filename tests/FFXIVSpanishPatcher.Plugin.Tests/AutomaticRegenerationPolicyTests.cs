using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class AutomaticRegenerationPolicyTests
{
    [Theory]
    [InlineData(RegenerationDecisionKind.InstallationMissing)]
    [InlineData(RegenerationDecisionKind.RegenerationRequired)]
    public void AutomaticWork_ReadyDependencies_CompletesBeforeGameLoad(RegenerationDecisionKind kind)
    {
        var decision = new RegenerationDecision(kind, "Pendiente.");

        Assert.True(AutomaticRegenerationPolicy.ShouldCompleteBeforeGameLoad(
            decision,
            StartupWait(DalamudStartupWaitStatus.Ready),
            Penumbra(ready: true)));
    }

    [Theory]
    [InlineData(RegenerationDecisionKind.FirstInstallation, DalamudStartupWaitStatus.Ready, true)]
    [InlineData(RegenerationDecisionKind.UpToDate, DalamudStartupWaitStatus.Ready, true)]
    [InlineData(RegenerationDecisionKind.InstallationMissing, DalamudStartupWaitStatus.Disabled, true)]
    [InlineData(RegenerationDecisionKind.InstallationMissing, DalamudStartupWaitStatus.Ready, false)]
    public void ManualOrUnsafeWork_DoesNotBlockStartup(
        RegenerationDecisionKind kind,
        DalamudStartupWaitStatus waitStatus,
        bool penumbraReady)
    {
        var decision = new RegenerationDecision(kind, "Estado.");

        Assert.False(AutomaticRegenerationPolicy.ShouldCompleteBeforeGameLoad(
            decision,
            StartupWait(waitStatus),
            Penumbra(penumbraReady)));
    }

    private static DalamudStartupWaitSnapshot StartupWait(DalamudStartupWaitStatus status)
        => new(status, "Carga temprana.");

    private static PenumbraAvailability Penumbra(bool ready)
        => ready
            ? new PenumbraAvailability(PenumbraAvailabilityStatus.Ready, 5, 15, "/mods", "Lista.")
            : new PenumbraAvailability(PenumbraAvailabilityStatus.Unavailable, 0, 0, null, "No disponible.");
}
