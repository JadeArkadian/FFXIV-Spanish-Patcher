using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FFXIVSpanishPatcher.App.ViewModels;
using FFXIVSpanishPatcher.App.Views;

namespace FFXIVSpanishPatcher.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainViewModel(window);
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            viewModel.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
