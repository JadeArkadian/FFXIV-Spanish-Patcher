using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFXIVSpanishPatcher.App.Services;
using FFXIVSpanishPatcher.App.ViewModels;

namespace FFXIVSpanishPatcher.App.Views;

public partial class MainWindow : Window, IShellServices
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Console.CollectionChanged += OnConsoleChanged;
        }
    }

    // Keep the console pinned to the latest line as the pipeline streams events.
    private void OnConsoleChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var scroll = this.FindControl<ScrollViewer>("ConsoleScroll");
        if (scroll is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => scroll.Offset = new Vector(scroll.Offset.X, scroll.Extent.Height),
            DispatcherPriority.Background);
    }

    public async Task<string?> PickGameFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecciona la carpeta de instalación de FFXIV",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public void RevealInFileManager(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"\"{path}\"");
            }
            else
            {
                Process.Start("xdg-open", $"\"{path}\"");
            }
        }
        catch
        {
            // Best-effort: opening the file manager must never crash the app.
        }
    }
}
