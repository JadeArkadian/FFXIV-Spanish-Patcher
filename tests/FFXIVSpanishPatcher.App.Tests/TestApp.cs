using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FFXIVSpanishPatcher.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

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
