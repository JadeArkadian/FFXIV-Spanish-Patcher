using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FFXIVSpanishPatcher.App.Services;
using FFXIVSpanishPatcher.App.ViewModels;
using FFXIVSpanishPatcher.App.Views;
using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.Translation;
using Xunit;

namespace FFXIVSpanishPatcher.App.Tests;

/// <summary>
/// Headless smoke test: instantiates and shows the real MainWindow bound to a real MainViewModel.
/// Catches runtime XAML failures (resource resolution, control templates, theme) that compile-time
/// binding validation cannot, with no display and no game install.
/// </summary>
public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_BuildsAndShowsWithoutErrors()
    {
        var entries = new List<TranslationEntry>
        {
            new() { Status = TranslationEntryStatus.Approved, Target = "x", SourceKey = new TranslationSourceKey { Sheet = "Item", RowId = 1 } },
            new() { Status = TranslationEntryStatus.Approved, Target = "y", SourceKey = new TranslationSourceKey { Sheet = "Quest", RowId = 2 } },
        };

        var viewModel = new MainViewModel(new NoopShell(), new ListTranslationSource(entries));
        var window = new MainWindow { DataContext = viewModel };

        window.Show();
        viewModel.Start();
        Dispatcher.UIThread.RunJobs();

        // The control tree built and the named console scroller resolved.
        Assert.NotNull(window.FindControl<ScrollViewer>("ConsoleScroll"));
        // Start() ran: it logged at least the game-path detection line synchronously.
        Assert.NotEmpty(viewModel.Console);
    }

    private sealed class NoopShell : IShellServices
    {
        public Task<string?> PickGameFolderAsync() => Task.FromResult<string?>(null);

        public Task CopyToClipboardAsync(string text) => Task.CompletedTask;

        public void RevealInFileManager(string path)
        {
        }
    }
}
