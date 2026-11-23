using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
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
        var host = this.FindControl<ContentControl>("MilestoneContentHost");
        if (host is not null)
        {
            var document = new TranslationMilestoneService().LoadOrFallback();
            host.Content = MarkdownAvaloniaRenderer.Render(document);
        }
    }

    /// <summary>The design size is larger than a small laptop screen: start inside the work area.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }

        // shortcut: 48 px stands in for the title bar and borders, which Avalonia does not report
        // before the window is shown; measure the frame if a platform ever needs more.
        var area = screen.WorkingArea;
        var width = Math.Max(MinWidth, Math.Min(Width, area.Width / screen.Scaling));
        var height = Math.Max(MinHeight, Math.Min(Height, (area.Height / screen.Scaling) - 48));
        if (width < Width || height < Height)
        {
            Width = width;
            Height = height;
            Position = new PixelPoint(
                area.X + Math.Max(0, (int)((area.Width - (width * screen.Scaling)) / 2)),
                area.Y);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // An overlay takes the keyboard with it, on the choice that changes nothing.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var target = e.PropertyName switch
        {
            nameof(MainViewModel.IsModalOpen) when ((MainViewModel)sender!).IsModalOpen => "ModalSecondary",
            nameof(MainViewModel.IsMilestoneOpen) when ((MainViewModel)sender!).IsMilestoneOpen => "MilestoneClose",
            _ => null,
        };
        if (target is not null)
        {
            Dispatcher.UIThread.Post(() => this.FindControl<Button>(target)?.Focus());
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsModalOpen)
        {
            viewModel.DismissModalCommand.Execute(null);
        }
        else if (viewModel.IsMilestoneOpen)
        {
            viewModel.CloseMilestonesCommand.Execute(null);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private void MilestoneBackdrop_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && DataContext is MainViewModel viewModel)
        {
            viewModel.CloseMilestonesCommand.Execute(null);
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
