using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FFXIVSpanishPatcher.App.Tests;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// Avalonia's headless render interface is process-wide, not per-test-thread: running
// [AvaloniaFact] tests in parallel xUnit workers races window/control-template construction
// (e.g. Path/StreamGeometry icon parsing) against "Unable to locate IPlatformRenderInterface".
// Forcing sequential execution eliminates the flake.
[assembly: ParallelizationAttribute(ParallelizationMode.None)]

namespace FFXIVSpanishPatcher.App.Tests;

/// <summary>Minimal headless application that loads the Fluent dark theme so the real window's
/// control templates resolve during smoke tests.</summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://FFXIVSpanishPatcher.App.Tests/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
