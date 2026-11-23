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
    public async Task GenerateMod_WhenTheOutputFolderCannotBeCreated_EndsAsAFailedRunInsteadOfThrowing()
    {
        using var install = TempGameInstall("2026.06.18.0000.0000");
        using var blocked = new BlockedFolder();
        var viewModel = ReadyViewModel(new CapturingShell(), recommendedGameVersion: null, blocked.Path);
        viewModel.Categories.Add(Category(selected: true));
        viewModel.GamePath = install.Root;

        await viewModel.GenerateModCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Equal(PatcherUiStage.Result, viewModel.Stage);
        Assert.False(viewModel.LastSuccess);
    }

    [AvaloniaFact]
    public void OpenOutputFolder_WhenTheFolderCannotBeCreated_ReportsItInsteadOfThrowing()
    {
        using var blocked = new BlockedFolder();
        var shell = new CapturingShell();
        var viewModel = ReadyViewModel(shell, recommendedGameVersion: null, blocked.Path);

        viewModel.OpenOutputFolderCommand.Execute(null);

        Assert.Null(shell.RevealedPath);
        Assert.Contains(
            viewModel.Console,
            line => line.Text.Contains("carpeta de salida", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void Preparation_DoesNotClaimSuccessBeforeThereIsAnyGameToCheck()
    {
        var viewModel = ReadyViewModel(new CapturingShell(), "2026.06.18.0000.0000");
        viewModel.Categories.Add(Category(selected: true));

        // No game yet: nothing is "compatible" and nothing is ready.
        Assert.False(viewModel.IsVersionMatch);
        Assert.True(viewModel.IsVersionUnknown);
        Assert.False(viewModel.IsReadyToGenerate);
        Assert.Equal("Falta completar la preparación", viewModel.ReadyHeadline);

        using var install = TempGameInstall("2026.06.18.0000.0000");
        viewModel.GamePath = install.Root;

        Assert.True(viewModel.IsVersionMatch);
        Assert.True(viewModel.IsReadyToGenerate);
        Assert.Equal("Listo para empezar", viewModel.ReadyHeadline);
    }

    [AvaloniaFact]
    public async Task Browse_WhileGenerating_KeepsThePathTheRunIsUsing()
    {
        using var install = TempGameInstall("2026.06.18.0000.0000");
        var viewModel = ReadyViewModel(new CapturingShell { PickResult = install.Root });
        viewModel.IsBusy = true;

        await viewModel.BrowseCommand.ExecuteAsync(null);

        Assert.Null(viewModel.GamePath);
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

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PluginInvite_AddsTheRepositoryAndInstallsThePluginOnlyAfterConfirmation(bool accept)
    {
        using var dalamud = new TempDalamud();
        var packages = new FakePluginPackageSource(TestPluginPackage.Create());
        var viewModel = InviteViewModel(new CapturingShell(), dalamud, gameRunning: false, packages);

        var invitation = viewModel.InvitePluginCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsModalOpen);
        Assert.Equal("Instalar plugin", viewModel.ModalPrimaryText);
        Assert.Contains("/main/dalamud/pluginmaster.json", viewModel.ModalExplanation, StringComparison.Ordinal);
        Assert.Contains("descargará", viewModel.ModalExplanation, StringComparison.Ordinal);
        Assert.Equal("{}", dalamud.Config);
        Assert.Equal(0, packages.Downloads);

        (accept ? viewModel.AcceptModalCommand : viewModel.DismissModalCommand).Execute(null);
        await invitation;

        Assert.Equal(accept, dalamud.Config.Contains("/main/dalamud/pluginmaster.json", StringComparison.Ordinal));
        Assert.Equal(accept ? 1 : 0, packages.Downloads);
        Assert.Equal(accept, dalamud.PluginInstalled);
        // Once done there is nothing left to offer; declining keeps the offer open.
        Assert.Equal(!accept, viewModel.IsPluginInviteVisible);
        Assert.Equal(
            accept,
            viewModel.Console.Any(line => line.Text.Contains("próximo inicio", StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public async Task PluginInvite_WhenTheDownloadFails_ChangesNothingAndCanBeRetried()
    {
        using var dalamud = new TempDalamud();
        var viewModel = InviteViewModel(
            new CapturingShell(),
            dalamud,
            gameRunning: false,
            new FakePluginPackageSource(package: null));

        var invitation = viewModel.InvitePluginCommand.ExecuteAsync(null);
        viewModel.AcceptModalCommand.Execute(null);
        await invitation;

        Assert.Equal("{}", dalamud.Config);
        Assert.False(dalamud.PluginInstalled);
        Assert.True(viewModel.IsPluginInviteVisible);
        Assert.Contains(viewModel.Console, line => line.Text.Contains("más tarde", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task PluginInvite_WhenItCanNoLongerBeDone_ChangesNothingAndHides()
    {
        using var dalamud = new TempDalamud();
        var packages = new FakePluginPackageSource(TestPluginPackage.Create());
        var viewModel = InviteViewModel(new CapturingShell(), dalamud, gameRunning: true, packages);

        await viewModel.InvitePluginCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsModalOpen);
        Assert.False(viewModel.IsPluginInviteVisible);
        Assert.Equal("{}", dalamud.Config);
        Assert.Equal(0, packages.Downloads);
        Assert.False(dalamud.PluginInstalled);
        Assert.Contains(viewModel.Console, line => line.Text.Contains("Cierra FFXIV", StringComparison.Ordinal));
    }

    private static MainViewModel InviteViewModel(
        CapturingShell shell,
        TempDalamud dalamud,
        bool gameRunning,
        IPluginPackageSource packages)
        => new(
            shell,
            new ListTranslationSource([]),
            recommendedGameVersion: null,
            dalamudService: new DalamudPenumbraService(isGameRunning: () => gameRunning, root: dalamud.Root),
            pluginPackages: packages);

    private static MainViewModel ReadyViewModel(CapturingShell shell)
        => ReadyViewModel(shell, recommendedGameVersion: null);

    private static MainViewModel ReadyViewModel(
        CapturingShell shell,
        string? recommendedGameVersion,
        string? outputFolder = null)
    {
        var viewModel = new MainViewModel(
            shell,
            new ListTranslationSource([]),
            recommendedGameVersion,
            outputFolder: outputFolder)
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

    /// <summary>A Dalamud root with an empty configuration, so no test touches the real one.</summary>
    private sealed class TempDalamud : IDisposable
    {
        public TempDalamud()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(ConfigPath, "{}");
        }

        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "ffxivsp-app-dalamud-" + Guid.NewGuid().ToString("N"));

        public string Config => File.ReadAllText(ConfigPath);

        public bool PluginInstalled => Directory.Exists(Path.Combine(Root, "installedPlugins"));

        private string ConfigPath => Path.Combine(Root, "dalamudConfig.json");

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>An output folder beneath a regular file: no platform can create it.</summary>
    private sealed class BlockedFolder : IDisposable
    {
        private readonly string _blocker = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ffxivsp-app-blocker-" + Guid.NewGuid().ToString("N"));

        public BlockedFolder() => File.WriteAllText(_blocker, string.Empty);

        public string Path => System.IO.Path.Combine(_blocker, "Output");

        public void Dispose() => File.Delete(_blocker);
    }

    private sealed class CapturingShell : IShellServices
    {
        public string? PickResult { get; init; }

        public string? RevealedPath { get; private set; }

        public Task<string?> PickGameFolderAsync() => Task.FromResult(PickResult);

        public Task CopyToClipboardAsync(string text) => Task.CompletedTask;

        public void RevealInFileManager(string path) => RevealedPath = path;
    }
}
