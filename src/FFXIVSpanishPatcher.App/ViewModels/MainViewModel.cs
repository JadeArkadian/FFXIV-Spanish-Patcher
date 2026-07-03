using System.Collections.ObjectModel;
using System.Reflection;
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
    private const string RecommendedGameVersionResourceName = "FFXIVSpanishPatcher.App.recommended-game-version.txt";
    private const string LandingPageUrl = "https://ffxivspanish.carrd.co/";

    private readonly IShellServices _shell;
    private readonly ITranslationSource _translations;
    private readonly string? _recommendedGameVersion;
    private readonly AppBuildInfo _buildInfo;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly bool _debugLogging;
    private IReadOnlyList<TranslationEntry>? _entries;
    private bool _updateCheckStarted;

    public MainViewModel(IShellServices shell, bool debugLogging = false)
        : this(
            shell,
            EmbeddedTranslationSource.FromAssemblyResource(typeof(MainViewModel).Assembly, ResourceName),
            LoadRecommendedGameVersion(typeof(MainViewModel).Assembly, RecommendedGameVersionResourceName),
            debugLogging: debugLogging)
    {
    }

    public MainViewModel(
        IShellServices shell,
        ITranslationSource translations,
        IUpdateCheckService? updateCheckService = null)
        : this(
            shell,
            translations,
            LoadRecommendedGameVersion(typeof(MainViewModel).Assembly, RecommendedGameVersionResourceName),
            updateCheckService)
    {
    }

    public MainViewModel(
        IShellServices shell,
        ITranslationSource translations,
        string? recommendedGameVersion,
        IUpdateCheckService? updateCheckService = null,
        AppBuildInfo? buildInfo = null,
        bool debugLogging = false)
    {
        _shell = shell;
        _translations = translations;
        _recommendedGameVersion = string.IsNullOrWhiteSpace(recommendedGameVersion)
            ? null
            : recommendedGameVersion.Trim();
        _buildInfo = buildInfo ?? AppBuildInfo.FromAssembly(typeof(MainViewModel).Assembly);
        _updateCheckService = updateCheckService ?? new GitHubReleaseUpdateCheckService(_buildInfo);
        _debugLogging = debugLogging;
        OutputFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FFXIVSpanish Patcher", "Output");
    }

    public ObservableCollection<CategoryViewModel> Categories { get; } = [];

    public ObservableCollection<ConsoleLine> Console { get; } = [];

    public string OutputFolder { get; }

    public string? RecommendedGameVersion => _recommendedGameVersion;

    public string AppVersionLabel => _buildInfo.DisplayVersion;

    public string WindowTitle => _buildInfo.WindowTitle;

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
        Console.Add(Info($"FFXIVSpanish Patcher {AppVersionLabel}"));
        if (_debugLogging)
        {
            Console.Add(Debug("Modo debug activado."));
        }

        GamePath = GamePathDetector.Detect();
        Console.Add(Info(GamePath is null
            ? "No se detectó la instalación de FFXIV. Indica la ruta manualmente."
            : $"Ruta del juego detectada: {GamePath}"));
        if (_recommendedGameVersion is not null)
        {
            Console.Add(Info($"Versión recomendada del juego: {_recommendedGameVersion}"));
        }

        if (!_updateCheckStarted)
        {
            _updateCheckStarted = true;
            _ = CheckForUpdatesOnceAsync();
        }

        _ = Task.Run(LoadTranslations);
    }

    private void LoadTranslations()
    {
        try
        {
            var entries = _translations.Load();
            var counts = entries
                .Where(e => PackageableStatus.IsPackageable(e, PackageableStatus.Default))
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

    private bool CanGenerate => !IsBusy && TranslationsReady;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateModAsync()
    {
        var enabled = Categories.Where(c => c.IsEnabled).ToList();
        var selected = enabled.Where(c => c.IsSelected).Select(c => c.Domain).ToArray();
        if (selected.Length == 0)
        {
            Reject("Selecciona al menos una categoría antes de generar el parche.");
            return;
        }

        if (!GamePathDetector.IsValid(GamePath))
        {
            Reject(InvalidGamePathMessage(GamePath));
            return;
        }

        WarnIfGameVersionDiffers();

        IsBusy = true;
        LastSuccess = null;
        StatusText = "Generando...";

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
            DebugLogging = _debugLogging,
            Meta = BuildPackageMeta(enabled),
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

    /// <summary>Penumbra meta.json fields shown in the mod browser. Version combines the patcher
    /// version with the FFXIV version installed on the user's machine — the one the .pmp was
    /// generated against (e.g. v0.1.0-2026.06.18.0000.0000) — and the description lists the
    /// domains selected for this build.</summary>
    private PackageMeta BuildPackageMeta(IReadOnlyList<CategoryViewModel> enabled)
    {
        var installedVersion = GamePathDetector.TryReadGameVersion(GamePath)?.Trim();
        var version = $"v{_buildInfo.PackageVersion}"
            + (string.IsNullOrEmpty(installedVersion) ? "" : $"-{installedVersion}");

        var domains = string.Join("\n", enabled.Where(c => c.IsSelected).Select(c => $"* {c.Label}"));
        var description = new PackageMeta().Description + $"\n\nCategorías incluidas:\n{domains}";

        return new PackageMeta
        {
            Version = version,
            Description = description,
            Website = LandingPageUrl,
        };
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await _shell.PickGameFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            GamePath = picked;
            if (GamePathDetector.IsValid(picked))
            {
                Console.Add(Info($"Ruta del juego: {picked}"));
            }
            else
            {
                var message = InvalidGamePathMessage(picked);
                Console.Add(Error(message));
            }
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

    private async Task CheckForUpdatesOnceAsync()
    {
        UpdateCheckResult result;
        try
        {
            result = await _updateCheckService.CheckAsync();
        }
        catch (Exception exception)
        {
            result = UpdateCheckResult.Unavailable(AppVersionLabel, exception.Message);
        }

        var line = UpdateCheckLine(result);
        if (line is not null)
        {
            Dispatcher.UIThread.Post(() => Console.Add(line));
        }
    }

    private static ConsoleLine Info(string message)
        => new(new PipelineEvent(PipelineComponent.Pipeline, message));

    private static ConsoleLine Debug(string message)
        => new(new PipelineEvent(PipelineComponent.Pipeline, message, PipelineLevel.Debug));

    private static ConsoleLine Warning(string message)
        => new(new PipelineEvent(PipelineComponent.Pipeline, message, PipelineLevel.Warning));

    private static ConsoleLine Error(string message)
        => new(new PipelineEvent(PipelineComponent.Pipeline, message, PipelineLevel.Error));

    private static ConsoleLine? UpdateCheckLine(UpdateCheckResult result)
        => result.Status switch
        {
            UpdateCheckStatus.Disabled => null,
            UpdateCheckStatus.UpToDate => Info(
                $"Parcheador al día: {result.CurrentVersion} (última publicada: {result.LatestVersion})."),
            UpdateCheckStatus.UpdateAvailable => Warning(
                $"Nueva versión disponible: {result.LatestVersion}. Descarga: {result.ReleaseUrl}"),
            UpdateCheckStatus.CurrentVersionUnknown => Warning(
                $"Última versión publicada: {result.LatestVersion}. Esta compilación ({result.CurrentVersion}) no se puede comparar."),
            _ => Warning("No se pudo comprobar actualizaciones; se continúa sin conexión."),
        };

    private void Reject(string message)
    {
        LastSuccess = false;
        StatusText = "ERROR";
        Console.Add(Error(message));
    }

    private void WarnIfGameVersionDiffers()
    {
        if (_recommendedGameVersion is null)
        {
            return;
        }

        var installedVersion = GamePathDetector.TryReadGameVersion(GamePath);
        if (string.IsNullOrWhiteSpace(installedVersion)
            || string.Equals(installedVersion.Trim(), _recommendedGameVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var message =
            $"Esta herramienta se compiló para FFXIV {_recommendedGameVersion}, " +
            $"pero la instalación seleccionada parece ser {installedVersion.Trim()}. " +
            "Puedes continuar, pero algunas traducciones podrían no aplicarse hasta que el parcheador se actualice.";
        Console.Add(Warning(message));
    }

    private static string InvalidGamePathMessage(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? "Selecciona la carpeta de instalación de FFXIV antes de generar el parche."
            : $"La ruta seleccionada no contiene datos válidos de FFXIV: {path}";

    private static string? LoadRecommendedGameVersion(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        var version = reader.ReadToEnd().Trim();
        return version.Length == 0 ? null : version;
    }
}
