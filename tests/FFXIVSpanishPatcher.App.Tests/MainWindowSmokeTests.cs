using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        var visualState = Environment.GetEnvironmentVariable("FFXIVSP_VISUAL_STATE");
        string? visualGameRoot = null;
        var entries = new List<TranslationEntry>
        {
            new() { Status = TranslationEntryStatus.Approved, Target = "x", SourceKey = new TranslationSourceKey { Sheet = "Item", RowId = 1 } },
            new() { Status = TranslationEntryStatus.Approved, Target = "y", SourceKey = new TranslationSourceKey { Sheet = "Quest", RowId = 2 } },
        };

        var viewModel = visualState is null
            ? new MainViewModel(
                new NoopShell(),
                new ListTranslationSource(entries),
                NullUpdateCheckService.Instance)
            : CreateVisualViewModel(entries, out visualGameRoot);
        var window = new MainWindow { DataContext = viewModel };
        // FFXIVSP_VISUAL_SIZE=1200x680 renders the screenshot at another window size.
        if (Environment.GetEnvironmentVariable("FFXIVSP_VISUAL_SIZE")?.Split('x') is [var width, var height])
        {
            window.Width = double.Parse(width, System.Globalization.CultureInfo.InvariantCulture);
            window.Height = double.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        }

        window.Show();
        if (visualState is null)
        {
            viewModel.Start();
        }

        switch (visualState)
        {
            case "advanced":
                viewModel.IsAdvancedOpen = true;
                break;
            case "modal":
                viewModel.ModalTitle = "Haz que Penumbra termine de cargar antes de iniciar FFXIV";
                viewModel.ModalBody =
                    "El patcher ha detectado Dalamud y Penumbra, pero Dalamud no está esperando a que los plugins terminen de cargar antes de continuar con el juego.";
                viewModel.ModalExplanation =
                    "¿Quieres corregirlo automáticamente? Se activará la opción «Esperar a que los plugins se carguen antes de iniciar el juego».";
                viewModel.ModalNote =
                    "Solo cambiará esta opción. No se modificarán Penumbra ni los archivos del juego.";
                viewModel.ModalPrimaryText = "Activar opción";
                viewModel.ModalSecondaryText = "Ahora no";
                viewModel.IsModalOpen = true;
                break;
            case "version-modal":
                viewModel.ModalTitle = "La versión del juego no coincide con esta traducción";
                viewModel.ModalBody =
                    "Este parcheador contiene traducciones preparadas para FFXIV 2026.07.16.0001.0000, pero la instalación seleccionada es 2025.01.01.0000.0000.";
                viewModel.ModalExplanation =
                    "Se intentará generar un mod utilizando los archivos que tienes instalados. Las hojas, páginas y líneas que no existan en esta versión se omitirán.";
                viewModel.ModalNote = "Al finalizar se indicará exactamente qué se ha podido aplicar.";
                viewModel.ModalPrimaryText = "Generar de todos modos";
                viewModel.ModalSecondaryText = "Volver";
                viewModel.IsModalOpen = true;
                break;
            case "milestones":
                viewModel.IsMilestoneOpen = true;
                break;
            case "invite":
                _ = viewModel.InvitePluginCommand.ExecuteAsync(null);
                break;
            case "generating":
                viewModel.Stage = PatcherUiStage.Generating;
                viewModel.ProgressPercent = 58;
                viewModel.ProgressText = "Paso 3 de 5 · Aplicando traducciones";
                viewModel.StatusText = "Generando";
                break;
            case "result":
                typeof(MainViewModel)
                    .GetField("_lastResult", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(
                        viewModel,
                        new PatchResult(
                            PatchOutcome.PackagedWithMisses,
                            "/tmp/FFXIVSpanish-preview.pmp",
                            new PatchStatistics(
                                AppliedWrites: 523_418,
                                RowMisses: 37,
                                MissingSheets: 2,
                                MissingSheetEntries: 91,
                                PatchedPages: 821)));
                viewModel.LastOutputName = "FFXIVSpanish-preview.pmp";
                viewModel.LastSuccess = true;
                viewModel.Stage = PatcherUiStage.Result;
                viewModel.StatusText = "Completado con omisiones";
                typeof(MainViewModel)
                    .GetMethod("NotifyResultProperties", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(viewModel, null);
                break;
            case "empty":
                viewModel.IsAdvancedOpen = true;
                foreach (var category in viewModel.Categories)
                {
                    category.IsSelected = false;
                }

                break;
        }

        // The control tree built and the virtualized console resolved.
        Assert.NotNull(window.FindControl<ConsoleLogTextBlock>("ConsoleText"));
        Assert.Contains(
            window.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Edición Endwalker");
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Edición Shadowbringers");
        Assert.Equal(
            "Empieza Endwalker.",
            window.FindControl<TextBlock>("MilestoneTitle")?.Text);
        Assert.NotNull(window.FindControl<Image>("MilestoneArrIcon")?.Source);
        Assert.NotNull(window.FindControl<Image>("MilestoneHeavenswardIcon")?.Source);
        Assert.NotNull(window.FindControl<Image>("MilestoneStormbloodIcon")?.Source);
        Assert.NotNull(window.FindControl<Image>("MilestoneShadowbringersIcon")?.Source);
        Assert.NotNull(window.FindControl<Image>("MilestoneEndwalkerIcon")?.Source);
        Assert.NotNull(window.FindControl<Button>("PluginInvite")?.Command);
        AssertVerticallyAligned(
            window,
            "GameCheckContent",
            "VersionCheckContent",
            "CorpusCheckContent",
            "DalamudCheckContent");
        // Start() ran: it logged at least the game-path detection line synchronously.
        Assert.NotEmpty(viewModel.Console);
        var screenshot = Environment.GetEnvironmentVariable("FFXIVSP_SCREENSHOT");
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (!string.IsNullOrWhiteSpace(screenshot))
        {
            frame.Save(screenshot, PngBitmapEncoderOptions.Default);
        }

        window.Close();
        if (visualGameRoot is not null)
        {
            Directory.Delete(visualGameRoot, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_SheenAnimationsStartAndStopCleanly()
    {
        var dalamudRoot = Path.Combine(Path.GetTempPath(), "ffxivsp-sheen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dalamudRoot);
        File.WriteAllText(Path.Combine(dalamudRoot, "dalamudConfig.json"), "{}");
        try
        {
            var viewModel = new MainViewModel(
                new NoopShell(),
                new ListTranslationSource([]),
                recommendedGameVersion: null,
                updateCheckService: NullUpdateCheckService.Instance,
                dalamudService: new DalamudPenumbraService(isGameRunning: () => false, root: dalamudRoot),
                pluginPackages: new FakePluginPackageSource(TestPluginPackage.Create()));
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            viewModel.Start();
            var progress = window.FindControl<ProgressBar>("GenerationProgress")!;
            var invite = window.FindControl<Button>("PluginInvite")!;

            // Starting or stopping a sheen swaps the animated brush with the resting one. If that
            // one is not a gradient too, Avalonia throws inside the binding, which then drops
            // the class and leaves the animation running.
            viewModel.Stage = PatcherUiStage.Generating;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("live", progress.Classes);

            viewModel.Stage = PatcherUiStage.Result;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("live", progress.Classes);

            // The invitation is offered while it can be carried out and leaves once it is done.
            Assert.True(invite.IsVisible);
            var invitation = viewModel.InvitePluginCommand.ExecuteAsync(null);
            viewModel.AcceptModalCommand.Execute(null);
            await invitation;
            Dispatcher.UIThread.RunJobs();
            Assert.False(invite.IsVisible);

            viewModel.Stage = PatcherUiStage.Generating;
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        finally
        {
            Directory.Delete(dalamudRoot, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_EscapeClosesOverlaysWithoutAcceptingThem()
    {
        var viewModel = new MainViewModel(
            new NoopShell(),
            new ListTranslationSource([]),
            NullUpdateCheckService.Instance);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        viewModel.ShowMilestonesCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<Button>("MilestoneClose")!.IsFocused);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(viewModel.IsMilestoneOpen);

        var answer = (Task<bool>)typeof(MainViewModel)
            .GetMethod("ShowDalamudModalAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(viewModel, null)!;
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<Button>("ModalSecondary")!.IsFocused);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(viewModel.IsModalOpen);
        Assert.False(await answer);
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_ExplainsThatCategoriesOnlySetPenumbraDefaults()
    {
        var viewModel = new MainViewModel(
            new NoopShell(),
            new ListTranslationSource([]),
            NullUpdateCheckService.Instance)
        {
            TranslationsReady = true,
            IsAdvancedOpen = true,
        };
        foreach (var category in CategoryCatalog.All)
        {
            viewModel.Categories.Add(new CategoryViewModel(category, count: 1));
        }

        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(textBlock => textBlock.Text).ToArray();
        Assert.Contains("Categorías activadas en primera importación de Penumbra", texts);
        Assert.Contains("El paquete siempre incluye traducción completa.", texts);
        Assert.DoesNotContain("Elige qué grupos incluir", texts);
        Assert.DoesNotContain("Categorías activadas al importar en Penumbra", texts);

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void MainWindow_MinimumSizeKeepsCorePanelsVisibleAtSupportedScales(double scale)
    {
        var viewModel = new MainViewModel(
            new NoopShell(),
            new ListTranslationSource([]),
            recommendedGameVersion: "2026.07.16.0001.0000",
            updateCheckService: NullUpdateCheckService.Instance)
        {
            TranslationsReady = true,
            IsAdvancedOpen = true,
        };
        foreach (var category in CategoryCatalog.All)
        {
            viewModel.Categories.Add(new CategoryViewModel(category, count: 167_968));
        }
        var window = new MainWindow
        {
            Width = 1200,
            Height = 680,
            DataContext = viewModel,
        };

        window.Show();
        window.SetRenderScaling(scale);
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width >= 1200 * scale);
        Assert.True(frame.PixelSize.Height >= 680 * scale);
        AssertInsideWindow(window, window.FindControl<Border>("SetupPanel"));
        AssertInsideWindow(window, window.FindControl<Border>("ConsolePanel"));
        AssertInsideWindow(window, window.FindControl<Button>("PrimaryAction"));
        AssertContained(
            window.FindControl<Border>("ModContentPanel"),
            window.FindControl<ScrollViewer>("ModContentScroll"));
        window.Close();
    }

    private static void AssertInsideWindow(Window window, Control? control)
    {
        Assert.NotNull(control);
        var origin = control.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(origin);
        Assert.InRange(origin.Value.X, 0, window.ClientSize.Width);
        Assert.InRange(origin.Value.Y, 0, window.ClientSize.Height);
        Assert.True(origin.Value.X + control.Bounds.Width <= window.ClientSize.Width + 0.5);
        Assert.True(origin.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 0.5);
    }

    private static void AssertVerticallyAligned(Window window, params string[] controlNames)
    {
        var centers = controlNames
            .Select(name =>
            {
                var control = window.FindControl<Grid>(name);
                Assert.NotNull(control);
                var origin = control.TranslatePoint(new Point(0, 0), window);
                Assert.NotNull(origin);
                return origin.Value.Y + (control.Bounds.Height / 2);
            })
            .ToArray();

        Assert.True(
            centers.Max() - centers.Min() <= 0.5,
            $"Las comprobaciones no comparten centro vertical: {string.Join(", ", centers)}");
    }

    private static void AssertContained(Control? parent, Control? child)
    {
        Assert.NotNull(parent);
        Assert.NotNull(child);
        var origin = child.TranslatePoint(new Point(0, 0), parent);
        Assert.NotNull(origin);
        Assert.InRange(origin.Value.X, 0, parent.Bounds.Width);
        Assert.InRange(origin.Value.Y, 0, parent.Bounds.Height);
        Assert.True(origin.Value.X + child.Bounds.Width <= parent.Bounds.Width + 0.5);
        Assert.True(origin.Value.Y + child.Bounds.Height <= parent.Bounds.Height + 0.5);
    }

    private static MainViewModel CreateVisualViewModel(
        IReadOnlyList<TranslationEntry> entries,
        out string gameRoot)
    {
        const string version = "2026.07.16.0001.0000";
        gameRoot = Path.Combine(Path.GetTempPath(), "ffxivsp-visual-game-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(gameRoot, "game");
        Directory.CreateDirectory(Path.Combine(game, "sqpack", "ffxiv"));
        File.WriteAllText(Path.Combine(game, "ffxivgame.ver"), version);

        var viewModel = new MainViewModel(
            new NoopShell(),
            new ListTranslationSource(entries),
            recommendedGameVersion: version,
            updateCheckService: NullUpdateCheckService.Instance,
            buildInfo: new AppBuildInfo(
                "v0.3.0",
                new AppReleaseVersion(0, 3, 0),
                "0.3.0",
                "JadeArkadian/FFXIV-Spanish-Patcher",
                "https://api.github.com/repos/JadeArkadian/FFXIV-Spanish-Patcher/releases/latest",
                "https://github.com/JadeArkadian/FFXIV-Spanish-Patcher/releases/latest"))
        {
            TranslationsReady = true,
            GamePath = gameRoot,
        };
        typeof(MainViewModel)
            .GetField("_entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(viewModel, entries);
        foreach (var category in CategoryCatalog.All)
        {
            viewModel.Categories.Add(new CategoryViewModel(category, count: 167_968));
        }

        viewModel.Console.Add(new ConsoleLine(new PipelineEvent(
            PipelineComponent.Pipeline,
            "FFXIVSpanish Patcher v0.3.0")));
        viewModel.Console.Add(new ConsoleLine(new PipelineEvent(
            PipelineComponent.Extractor,
            "Archivos base del juego verificados",
            PipelineLevel.Ok)));
        viewModel.Console.Add(new ConsoleLine(new PipelineEvent(
            PipelineComponent.Patcher,
            "Traducciones cargadas: 523.418 entradas",
            PipelineLevel.Ok)));
        return viewModel;
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
