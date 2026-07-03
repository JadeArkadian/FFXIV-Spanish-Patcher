using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FFXIVSpanishPatcher.App.Tests;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// Avalonia's headless render interface is process-wide, not per-test-thread: running
// [AvaloniaFact] tests in parallel xUnit workers races window/control-template construction
// (e.g. Path/StreamGeometry icon parsing) against "Unable to locate IPlatformRenderInterface".
// Forcing sequential execution eliminates the flake.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FFXIVSpanishPatcher.App.Tests;

/// <summary>Minimal headless application that loads the Fluent dark theme so the real window's
/// control templates resolve during smoke tests.</summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
