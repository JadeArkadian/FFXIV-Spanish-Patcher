using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FFXIVSpanishPatcher.App.Services;

namespace FFXIVSpanishPatcher.App.Views;

public partial class MainWindow : Window, IShellServices
{
    public MainWindow()
    {
        InitializeComponent();
        var host = this.FindControl<ContentControl>("MilestoneContentHost");
        if (host is not null)
        {
            var document = new TranslationMilestoneService().LoadOrFallback();
            host.Content = MarkdownAvaloniaRenderer.Render(document);
        }
    }

    private void SelectConsoleAll_OnClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<ConsoleLogTextBlock>("ConsoleText")?.SelectAll();
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
