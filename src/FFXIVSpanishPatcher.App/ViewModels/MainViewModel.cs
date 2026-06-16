using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVSpanishPatcher.App.Services;
using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.App.ViewModels;

/// <summary>
/// View model for the main window. Detects the game path, loads the embedded translations to build
/// the category list, and drives a <see cref="PatchPipeline"/> run while streaming progress to the
/// console.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const string ResourceName = "FFXIVSpanishPatcher.App.translations.dat";

    private readonly IShellServices _shell;
    private readonly ITranslationSource _translations;
    private IReadOnlyList<TranslationEntry>? _entries;

    public MainViewModel(IShellServices shell)
        : this(shell, EmbeddedTranslationSource.FromAssemblyResource(typeof(MainViewModel).Assembly, ResourceName))
    {
    }

    public MainViewModel(IShellServices shell, ITranslationSource translations)
    {
        _shell = shell;
        _translations = translations;
        OutputFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FFXIVSpanish Patcher", "Output");
    }

    public ObservableCollection<CategoryViewModel> Categories { get; } = [];

    public ObservableCollection<ConsoleLine> Console { get; } = [];

    public string OutputFolder { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateModCommand))]
    private string? gamePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateModCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateModCommand))]
    private bool translationsReady;

    [ObservableProperty]
    private bool verifyIntegrity = true;

    [ObservableProperty]
    private string? lastOutputName;

    [ObservableProperty]
    private bool? lastSuccess;

    [ObservableProperty]
    private string statusText = "Listo";

    /// <summary>Kicks off path detection and the background translation load. Called once after the
    /// window's DataContext is set.</summary>
    public void Start()
    {
        GamePath = GamePathDetector.Detect();
        Console.Add(Info(GamePath is null
            ? "No se detectó la instalación de FFXIV. Indica la ruta manualmente."
            : $"Ruta del juego detectada: {GamePath}"));

        _ = Task.Run(LoadTranslations);
    }

    private void LoadTranslations()
    {
        try
        {
            var entries = _translations.Load();
            var counts = entries
                .Where(e => string.Equals(e.Status, TranslationEntryStatus.Approved, StringComparison.OrdinalIgnoreCase))
                .GroupBy(TranslationCategories.DomainOf)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            Dispatcher.UIThread.Post(() =>
            {
                _entries = entries;
                Categories.Clear();
                foreach (var info in CategoryCatalog.All)
                {
                    Categories.Add(new CategoryViewModel(info, counts.GetValueOrDefault(info.Domain)));
                }

                TranslationsReady = true;
                Console.Add(Info($"Traducciones cargadas: {entries.Count} entradas."));
            });
        }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() => Console.Add(Error($"No se pudieron cargar las traducciones: {exception.Message}")));
        }
    }

    private bool CanGenerate => !IsBusy && TranslationsReady && GamePathDetector.IsValid(GamePath);

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateModAsync()
    {
        IsBusy = true;
        LastSuccess = null;
        StatusText = "Generando...";

        var enabled = Categories.Where(c => c.IsEnabled).ToList();
        var selected = enabled.Where(c => c.IsSelected).Select(c => c.Domain).ToArray();
        IReadOnlyCollection<string>? categories = selected.Length == enabled.Count ? null : selected;

        Directory.CreateDirectory(OutputFolder);
        var outputName = $"FFXIVSpanish-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.pmp";
        var request = new PatchRequest
        {
            GamePath = GamePath,
            Categories = categories,
            OutputPath = Path.Combine(OutputFolder, outputName),
            StagingPath = Path.Combine(Path.GetTempPath(), "ffxivsp-patcher-staging"),
            VerifyIntegrity = VerifyIntegrity,
        };

        // Progress is created on the UI thread, so its callbacks marshal back here automatically.
        var progress = new Progress<PipelineEvent>(e => Console.Add(new ConsoleLine(e)));
        var pipeline = PatchPipeline.ForClient(new ListTranslationSource(_entries!));

        PatchResult result;
        try
        {
            result = await Task.Run(() => pipeline.Run(request, progress));
        }
        catch (Exception exception)
        {
            Console.Add(Error($"Error inesperado: {exception.Message}"));
            result = new PatchResult(PatchOutcome.ValidationFailed, null, 0, 0, 0, 0);
        }

        LastSuccess = result.Success;
        StatusText = result.Success ? "ÉXITO" : "ERROR";
        if (result.Success)
        {
            LastOutputName = outputName;
        }

        IsBusy = false;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await _shell.PickGameFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            GamePath = picked;
            Console.Add(GamePathDetector.IsValid(picked)
                ? Info($"Ruta del juego: {picked}")
                : Error($"La ruta no contiene datos válidos de FFXIV: {picked}"));
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        Directory.CreateDirectory(OutputFolder);
        _shell.RevealInFileManager(OutputFolder);
    }

    [RelayCommand]
    private void ClearConsole() => Console.Clear();

    [RelayCommand]
    private async Task CopyLogAsync()
        => await _shell.CopyToClipboardAsync(string.Join(Environment.NewLine, Console.Select(l => l.Text)));

    private static ConsoleLine Info(string message)
        => new(new PipelineEvent(PipelineComponent.Pipeline, message));

    private static ConsoleLine Error(string message)
        => new(new PipelineEvent(PipelineComponent.Pipeline, message, PipelineLevel.Error));
}
