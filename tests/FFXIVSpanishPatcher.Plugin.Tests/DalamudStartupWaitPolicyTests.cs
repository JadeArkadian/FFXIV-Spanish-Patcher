using Dalamud.Plugin;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class DalamudStartupWaitPolicyTests
{
    [Theory]
    [InlineData(PluginLoadReason.Boot, true)]
    [InlineData(PluginLoadReason.Installer, false)]
    [InlineData(PluginLoadReason.Update, false)]
    [InlineData(PluginLoadReason.Reload, false)]
    [InlineData(PluginLoadReason.Unknown, false)]
    public void CurrentSessionGuarantee_RequiresBootLoad(PluginLoadReason reason, bool expected)
        => Assert.Equal(
            expected,
            DalamudStartupWaitPolicy.IsGuaranteedForCurrentSession(enabled: true, reason));

    [Theory]
    [InlineData(false, false, DalamudStartupWaitStatus.Disabled)]
    [InlineData(true, false, DalamudStartupWaitStatus.RestartRequired)]
    [InlineData(true, true, DalamudStartupWaitStatus.Ready)]
    public void Evaluate_RequiresEnabledSettingAndCurrentSessionGuarantee(
        bool enabled,
        bool guaranteed,
        DalamudStartupWaitStatus expected)
    {
        var snapshot = DalamudStartupWaitPolicy.Evaluate(enabled, guaranteed);

        Assert.Equal(expected, snapshot.Status);
    }

    [Fact]
    public void Evaluate_UnavailableCannotBeTreatedAsReady()
    {
        var snapshot = DalamudStartupWaitPolicy.Evaluate(null, guaranteedForCurrentSession: false);

        Assert.Equal(DalamudStartupWaitStatus.Unavailable, snapshot.Status);
        Assert.False(snapshot.IsReady);
        Assert.False(snapshot.CanEnable);
    }
}
