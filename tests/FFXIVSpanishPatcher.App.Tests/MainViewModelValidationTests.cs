using Avalonia.Headless.XUnit;
using FFXIVSpanishPatcher.App.Services;
using FFXIVSpanishPatcher.App.ViewModels;
using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.App.Tests;

public sealed class MainViewModelValidationTests
{
    [AvaloniaFact]
    public async Task GenerateMod_WithNoSelectedCategories_LogsConsoleError()
    {
        var shell = new CapturingShell();
        var viewModel = ReadyViewModel(shell);
        viewModel.Categories.Add(Category(selected: false));

        await viewModel.GenerateModCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.Console, line => line.Text.Contains("categoría", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task GenerateMod_WithInvalidGamePath_LogsConsoleError()
    {
        var shell = new CapturingShell();
        var viewModel = ReadyViewModel(shell);
        viewModel.GamePath = @"C:\does\not\contain\ffxiv";
        viewModel.Categories.Add(Category(selected: true));

        await viewModel.GenerateModCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.Console, line => line.Text.Contains("no contiene datos válidos", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task Browse_WithInvalidGamePath_LogsConsoleError()
    {
        var picked = Path.Combine(Path.GetTempPath(), "ffxivsp-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(picked);
        try
        {
            var shell = new CapturingShell { PickResult = picked };
            var viewModel = ReadyViewModel(shell);

            await viewModel.BrowseCommand.ExecuteAsync(null);

            Assert.Contains(viewModel.Console, line => line.Text.Contains(picked, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(picked, recursive: true);
        }
    }

    private static MainViewModel ReadyViewModel(CapturingShell shell)
    {
        var viewModel = new MainViewModel(shell, new ListTranslationSource([]), recommendedGameVersion: null)
        {
            TranslationsReady = true,
        };
        return viewModel;
    }

    private static CategoryViewModel Category(bool selected)
    {
        var category = new CategoryViewModel(new CategoryInfo("items", "Objetos", "Objetos"), count: 1)
        {
            IsSelected = selected,
        };
        return category;
    }

    private sealed class CapturingShell : IShellServices
    {
        public string? PickResult { get; init; }

        public Task<string?> PickGameFolderAsync() => Task.FromResult(PickResult);

        public Task CopyToClipboardAsync(string text) => Task.CompletedTask;

        public void RevealInFileManager(string path)
        {
        }
    }
}
