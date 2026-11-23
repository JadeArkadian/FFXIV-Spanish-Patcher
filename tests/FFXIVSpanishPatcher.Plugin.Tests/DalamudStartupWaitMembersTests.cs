using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

/// <summary>
/// The manager reads and switches on Dalamud's "wait for plugins" setting through members that are
/// not public API. This runs against the Dalamud build the plugin is compiled with, so a Dalamud
/// update that renames one fails here instead of in every user's game.
/// </summary>
public sealed class DalamudStartupWaitMembersTests
{
    // Without these the manager cannot tell whether early loading is on, and it then refuses to
    // install or regenerate the translation at all.
    [Fact]
    public void Dalamud_StillHasWhatReadingAndEnablingTheSettingNeeds()
    {
        // Dalamud's types mention its own dependencies, which only the game process has loaded.
        // Here they come from the folder the tests were built against.
        var dalamudFolder = typeof(DalamudStartupWaitMembersTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "DalamudLibPath")
            .Value!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(dalamudFolder, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };

        var members = DalamudStartupWaitMembers.Resolve(typeof(IDalamudPluginInterface).Assembly);

        Assert.NotNull(members.ConfigurationGetter);
        Assert.True(members.ConfigurationGetter.IsStatic);
        Assert.Empty(members.ConfigurationGetter.GetParameters());
        Assert.NotNull(members.WaitProperty);
        Assert.Equal(typeof(bool), members.WaitProperty.PropertyType);
        Assert.True(members.WaitProperty.CanRead && members.WaitProperty.CanWrite);
        Assert.NotNull(members.SaveConfiguration);
        Assert.Empty(members.SaveConfiguration.GetParameters());
    }
}
