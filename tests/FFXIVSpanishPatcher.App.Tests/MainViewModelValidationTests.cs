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

    [AvaloniaFact]
    public async Task DeliberatelyOldGameVersion_IsWarnedAndCanReturnWithoutGenerating()
    {
        using var install = TempGameInstall("2025.01.01.0000.0000");
        var viewModel = ReadyViewModel(new CapturingShell(), "2026.06.18.0000.0000");
        viewModel.Categories.Add(Category(selected: true));
        viewModel.GamePath = install.Root;

        Assert.Equal(GameVersionCompatibility.Different, viewModel.VersionCompatibility);
        Assert.True(viewModel.IsVersionWarning);
        Assert.Equal("Versión diferente", viewModel.VersionCheckTitle);

        var generation = viewModel.GenerateModCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsModalOpen);
        Assert.Contains("no coincide", viewModel.ModalTitle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hojas, páginas y líneas", viewModel.ModalExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Generar de todos modos", viewModel.ModalPrimaryText);
        Assert.Equal("Volver", viewModel.ModalSecondaryText);

        viewModel.DismissModalCommand.Execute(null);
        await generation;

        Assert.Equal(PatcherUiStage.Preparation, viewModel.Stage);
        Assert.DoesNotContain(
            viewModel.Console,
            line => line.Text.Contains("best effort", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task DeliberatelyOldGameVersion_CanConfirmBestEffort()
    {
        using var install = TempGameInstall("2025.01.01.0000.0000");
        var viewModel = ReadyViewModel(new CapturingShell(), "2026.06.18.0000.0000");
        viewModel.Categories.Add(Category(selected: true));
        viewModel.GamePath = install.Root;

        var generation = viewModel.GenerateModCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsModalOpen);

        viewModel.AcceptModalCommand.Execute(null);
        await generation;

        Assert.Contains(
            viewModel.Console,
            line => line.Text.Contains("best effort", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PatcherUiStage.Result, viewModel.Stage);
        Assert.False(viewModel.LastSuccess);
    }

    [AvaloniaFact]
    public void CategoryCommands_KeepAtLeastOneSelectionRequired()
    {
        var viewModel = ReadyViewModel(new CapturingShell());
        viewModel.Categories.Add(Category(selected: true));
        viewModel.Categories.Add(Category(selected: true));

        viewModel.SelectNoCategoriesCommand.Execute(null);

        Assert.False(viewModel.HasSelectedCategories);
        Assert.True(viewModel.ShowCategorySelectionError);
        Assert.False(viewModel.GenerateModCommand.CanExecute(null));

        viewModel.SelectAllCategoriesCommand.Execute(null);

        Assert.True(viewModel.HasSelectedCategories);
        Assert.False(viewModel.ShowCategorySelectionError);
    }

    private static MainViewModel ReadyViewModel(CapturingShell shell)
        => ReadyViewModel(shell, recommendedGameVersion: null);

    private static MainViewModel ReadyViewModel(CapturingShell shell, string? recommendedGameVersion)
    {
        var viewModel = new MainViewModel(
            shell,
            new ListTranslationSource([]),
            recommendedGameVersion)
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

    private static TempInstall TempGameInstall(string version)
    {
        var root = Path.Combine(Path.GetTempPath(), "ffxivsp-app-game-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "sqpack", "ffxiv"));
        File.WriteAllText(Path.Combine(game, "ffxivgame.ver"), version + Environment.NewLine);
        return new TempInstall(root);
    }

    private sealed class TempInstall(string root) : IDisposable
    {
        public string Root { get; } = root;

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
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
