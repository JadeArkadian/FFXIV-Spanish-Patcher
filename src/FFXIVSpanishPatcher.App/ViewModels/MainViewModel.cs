using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVSpanishPatcher.App.Services;
using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.App.ViewModels;

public enum PatcherUiStage
{
    Preparation,
    Generating,
    Result,
}

public enum GameVersionCompatibility
{
    Unknown,
    Match,
    Different,
}

/// <summary>Main application state. UI remains a thin projection over pipeline and environment services.</summary>
public partial class MainViewModel : ObservableObject
{
    private const string TranslationResourceName = "FFXIVSpanishPatcher.App.translations.dat";
    private const string ExternalTranslationsMetadataName = "ExternalTranslations";
    private const string ExternalTranslationsFileName = "translations.dat";
    private const string RecommendedGameVersionResourceName = "FFXIVSpanishPatcher.App.recommended-game-version.txt";
    private const string LandingPageUrl = "https://ffxivspanish.carrd.co/";

    private readonly IShellServices _shell;
    private readonly ITranslationSource _translations;
    private readonly string? _recommendedGameVersion;
    private readonly AppBuildInfo _buildInfo;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly DalamudPenumbraService _dalamudService;
    private readonly bool _debugLogging;
    private IReadOnlyList<TranslationEntry>? _entries;
    private DalamudPenumbraCheck _dalamudCheck = new(DalamudPenumbraState.NotDetected);
    private PatchResult? _lastResult;
    private TaskCompletionSource<bool>? _modalCompletion;
    private bool _updateCheckStarted;
    private bool _dalamudPromptHandled;
    private bool _started;

    public MainViewModel(IShellServices shell, bool debugLogging = false)
        : this(
            shell,
            CreateDefaultTranslationSource(),
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
        bool debugLogging = false,
        DalamudPenumbraService? dalamudService = null)
    {
        _shell = shell;
        _translations = translations;
        _recommendedGameVersion = string.IsNullOrWhiteSpace(recommendedGameVersion)
            ? null
            : recommendedGameVersion.Trim();
        _buildInfo = buildInfo ?? AppBuildInfo.FromAssembly(typeof(MainViewModel).Assembly);
        _updateCheckService = updateCheckService ?? new GitHubReleaseUpdateCheckService(_buildInfo);
        _dalamudService = dalamudService ?? new DalamudPenumbraService();
        _debugLogging = debugLogging;
        OutputFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FFXIVSpanish Patcher",
            "Output");
        Categories.CollectionChanged += OnCategoriesChanged;
    }

    public ObservableCollection<CategoryViewModel> Categories { get; } = [];
    public ObservableCollection<ConsoleLine> Console { get; } = [];
    public string OutputFolder { get; }
    public string? RecommendedGameVersion => _recommendedGameVersion;
    public string AppVersionLabel => _buildInfo.DisplayVersion;
    public string WindowTitle => _buildInfo.WindowTitle;
    public string CorpusFooterLabel => _recommendedGameVersion ?? "incluido";

    public int AvailableCategoryCount => Categories.Count(category => category.IsEnabled);
    public int SelectedCategoryCount => Categories.Count(category => category.IsEnabled && category.IsSelected);
    public bool HasSelectedCategories => SelectedCategoryCount > 0;
    public string CategorySummary =>
        $"{SelectedCategoryCount} de {AvailableCategoryCount} categorías seleccionadas";
    public bool ShowCategorySelectionError => TranslationsReady && !HasSelectedCategories;
    public bool IsAdvancedClosed => !IsAdvancedOpen;

    public bool IsGamePathValid => GamePathDetector.IsValid(GamePath);
    public bool IsGameReady => IsGamePathValid;
    public string GameCheckTitle => IsGamePathValid ? "FFXIV detectado" : "Selecciona FFXIV";
    public string GameCheckSubtitle => IsGamePathValid ? "Ruta válida" : "Ruta pendiente";
    public string VersionCheckTitle => VersionCompatibility switch
    {
        GameVersionCompatibility.Match => "Versión compatible",
        GameVersionCompatibility.Different => "Versión diferente",
        _ => "Versión desconocida",
    };
    public string VersionCheckSubtitle => InstalledGameVersion
        ?? (_recommendedGameVersion is null ? "Sin referencia" : $"Esperada: {_recommendedGameVersion}");
    public bool IsVersionWarning => VersionCompatibility == GameVersionCompatibility.Different;
    public bool IsVersionOk => !IsVersionWarning;
    public string CorpusCheckTitle => TranslationsReady ? "Corpus verificado" : "Cargando corpus";
    public string CorpusCheckSubtitle => _entries is null ? "Preparando…" : $"{_entries.Count:N0} entradas";
    public string DalamudCheckTitle => _dalamudCheck.State switch
    {
        DalamudPenumbraState.Ready => "Penumbra preparado",
        DalamudPenumbraState.RequiresResumeAfterPluginLoad => "Revisar Penumbra",
        _ => "Penumbra opcional",
    };
    public string DalamudCheckSubtitle => _dalamudCheck.State switch
    {
        DalamudPenumbraState.Ready => "Dalamud espera a los plugins",
        DalamudPenumbraState.RequiresResumeAfterPluginLoad => "Revisar ajuste de Dalamud",
        _ => "Sin comprobación disponible",
    };
    public bool IsDalamudWarning =>
        _dalamudCheck.State == DalamudPenumbraState.RequiresResumeAfterPluginLoad;
    public bool IsDalamudOk => !IsDalamudWarning;

    public bool IsPreparationStage => Stage == PatcherUiStage.Preparation;
    public bool IsGeneratingStage => Stage == PatcherUiStage.Generating;
    public bool IsResultStage => Stage == PatcherUiStage.Result;
    public bool IsPastPreparation => Stage != PatcherUiStage.Preparation;
    public bool IsBeforeGenerating => Stage == PatcherUiStage.Preparation;
    public bool IsPastGenerating => Stage == PatcherUiStage.Result;
    public bool IsResultSuccess => IsResultStage && _lastResult?.Outcome == PatchOutcome.Ok;
    public bool IsResultPartial => IsResultStage && _lastResult?.Outcome == PatchOutcome.PackagedWithMisses;
    public bool IsResultError => IsResultStage && _lastResult is { Success: false };
    public bool IsUsableResult => IsResultSuccess || IsResultPartial;
    public string ResultTitle => _lastResult?.Outcome switch
    {
        PatchOutcome.Ok => "Mod creado y verificado",
        PatchOutcome.PackagedWithMisses => "Mod verificado con omisiones",
        PatchOutcome.ValidationFailed => "La verificación ha fallado",
        PatchOutcome.Contaminated => "La base no es compatible",
        PatchOutcome.GameDataError => "No se pudieron leer los datos del juego",
        PatchOutcome.NothingToPackage => "No se pudo aplicar ninguna traducción",
        _ => "No se pudo crear el mod",
    };
    public string ResultSubtitle => _lastResult?.Success == true
        ? LastOutputName ?? string.Empty
        : "No se ha publicado un paquete nuevo. Consulta la consola.";
    public string ResultApplied => (_lastResult?.Statistics.AppliedWrites ?? 0).ToString("N0");
    public string ResultPages => (_lastResult?.Statistics.PatchedPages ?? 0).ToString("N0");
    public string ResultOmitted => (_lastResult?.Statistics.SkippedEntries ?? 0).ToString("N0");
    public string ResultFailures => _lastResult?.Success == true ? "0" : "1";

    public string SetupTitle => IsGamePathValid && TranslationsReady
        ? "Todo preparado para crear el mod"
        : "Completa la preparación del mod";
    public string SetupLead => IsGamePathValid && TranslationsReady
        ? "Instalación, versión, corpus y entorno de Penumbra revisados."
        : "Selecciona una instalación válida mientras se prepara el corpus.";

    public string LegalNoticeText =>
        "Proyecto no oficial hecho por fans. No está afiliado, patrocinado ni aprobado por Square Enix.\n\n" +
        "FINAL FANTASY XIV, FFXIV, SQUARE ENIX y todos los nombres, marcas, logotipos, textos y demás " +
        "propiedad intelectual relacionados pertenecen a sus respectivos propietarios.\n\n" +
        "Esta aplicación no incluye ni redistribuye archivos del juego: trabaja únicamente sobre los " +
        "archivos de tu instalación local y genera un paquete separado para Penumbra.\n\n" +
        "No elude la propiedad del juego, suscripciones, autenticación ni restricciones de licencia. " +
        "Eres responsable de cumplir los términos y políticas aplicables.\n\n" +
        "Las traducciones pertenecen a sus autores. La licencia MIT aplica solo al código fuente. " +
        "Este software se proporciona sin garantía; aviso completo en NOTICE.md.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateModCommand))]
    private string? gamePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateModCommand))]
    [NotifyPropertyChangedFor(nameof(IsPreparationStage))]
    [NotifyPropertyChangedFor(nameof(IsGeneratingStage))]
    [NotifyPropertyChangedFor(nameof(IsResultStage))]
    [NotifyPropertyChangedFor(nameof(IsPastPreparation))]
    [NotifyPropertyChangedFor(nameof(IsBeforeGenerating))]
    [NotifyPropertyChangedFor(nameof(IsPastGenerating))]
    [NotifyPropertyChangedFor(nameof(IsResultSuccess))]
    [NotifyPropertyChangedFor(nameof(IsResultPartial))]
    [NotifyPropertyChangedFor(nameof(IsResultError))]
    [NotifyPropertyChangedFor(nameof(IsUsableResult))]
    private PatcherUiStage stage = PatcherUiStage.Preparation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateModCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateModCommand))]
    private bool translationsReady;

    [ObservableProperty]
    private string? installedGameVersion;

    [ObservableProperty]
    private GameVersionCompatibility versionCompatibility;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdvancedClosed))]
    private bool isAdvancedOpen;

    [ObservableProperty]
    private bool isMilestoneOpen;

    [ObservableProperty]
    private string? lastOutputName;

    [ObservableProperty]
    private bool? lastSuccess;

    [ObservableProperty]
    private string statusText = "Preparado";

    [ObservableProperty]
    private int progressPercent;

    [ObservableProperty]
    private string progressText = "Preparando";

    [ObservableProperty]
    private bool isModalOpen;

    [ObservableProperty]
    private string modalTitle = string.Empty;

    [ObservableProperty]
    private string modalBody = string.Empty;

    [ObservableProperty]
    private string modalExplanation = string.Empty;

    [ObservableProperty]
    private string modalNote = string.Empty;

    [ObservableProperty]
    private string modalPrimaryText = string.Empty;

    [ObservableProperty]
    private string modalSecondaryText = string.Empty;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
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
            Console.Add(Info($"Versión de referencia del corpus: {_recommendedGameVersion}"));
        }

        _dalamudCheck = _dalamudService.Inspect();
        NotifyEnvironmentProperties();

        if (!_updateCheckStarted)
        {
            _updateCheckStarted = true;
            _ = CheckForUpdatesOnceAsync();
        }

        _ = Task.Run(LoadTranslations);
    }

    partial void OnGamePathChanged(string? value)
    {
        RefreshGameState();
        GenerateModCommand.NotifyCanExecuteChanged();
    }

    partial void OnTranslationsReadyChanged(bool value)
    {
        NotifyPreparationProperties();
        GenerateModCommand.NotifyCanExecuteChanged();
    }

    private void RefreshGameState()
    {
        InstalledGameVersion = null;
        VersionCompatibility = GameVersionCompatibility.Unknown;
        if (IsGamePathValid)
        {
            try
            {
                InstalledGameVersion = GamePathDetector.TryReadGameVersion(GamePath)?.Trim();
            }
            catch
            {
                InstalledGameVersion = null;
            }

            if (_recommendedGameVersion is not null && InstalledGameVersion is not null)
            {
                VersionCompatibility = string.Equals(
                    InstalledGameVersion,
                    _recommendedGameVersion,
                    StringComparison.OrdinalIgnoreCase)
                    ? GameVersionCompatibility.Match
                    : GameVersionCompatibility.Different;
            }
        }

        OnPropertyChanged(nameof(IsGamePathValid));
        OnPropertyChanged(nameof(IsGameReady));
        OnPropertyChanged(nameof(GameCheckTitle));
        OnPropertyChanged(nameof(GameCheckSubtitle));
        OnPropertyChanged(nameof(VersionCheckTitle));
        OnPropertyChanged(nameof(VersionCheckSubtitle));
        OnPropertyChanged(nameof(IsVersionWarning));
        OnPropertyChanged(nameof(IsVersionOk));
        NotifyPreparationProperties();
    }

    private void LoadTranslations()
    {
        try
        {
            var entries = _translations.Load();
            var counts = entries
                .Where(entry => PackageableStatus.IsPackageable(entry, PackageableStatus.Default))
                .GroupBy(TranslationCategories.DomainOf)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            Dispatcher.UIThread.Post(() =>
            {
                _entries = entries;
                Categories.Clear();
                foreach (var info in CategoryCatalog.All)
                {
                    Categories.Add(new CategoryViewModel(info, counts.GetValueOrDefault(info.Domain)));
                }

                TranslationsReady = true;
                OnPropertyChanged(nameof(CorpusCheckSubtitle));
                Console.Add(Info($"Traducciones cargadas: {entries.Count:N0} entradas."));
            });
        }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Console.Add(Error($"No se pudieron cargar las traducciones: {exception.Message}"));
                Stage = PatcherUiStage.Result;
                LastSuccess = false;
                StatusText = "Error";
                NotifyResultProperties();
            });
        }
    }

    private bool CanGenerate =>
        !IsBusy && TranslationsReady && IsGamePathValid && HasSelectedCategories;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateModAsync()
    {
        var enabled = Categories.Where(category => category.IsEnabled).ToList();
        var selected = enabled.Where(category => category.IsSelected).Select(category => category.Domain).ToArray();
        if (selected.Length == 0)
        {
            Reject("Selecciona al menos una categoría antes de generar el mod.");
            return;
        }

        if (!GamePathDetector.IsValid(GamePath))
        {
            Reject(InvalidGamePathMessage(GamePath));
            return;
        }

        var compatibilityMode = PatchCompatibilityMode.Strict;
        if (VersionCompatibility == GameVersionCompatibility.Different)
        {
            var continueWithMismatch = await ShowVersionMismatchModalAsync();
            if (!continueWithMismatch)
            {
                return;
            }

            compatibilityMode = PatchCompatibilityMode.BestEffortVersionMismatch;
            Console.Add(Warning(
                $"Versión distinta confirmada: corpus {_recommendedGameVersion}; instalación {InstalledGameVersion}. " +
                "Se aplicará best effort y se informarán todas las omisiones."));
        }

        if (!_dalamudPromptHandled
            && _dalamudCheck.State == DalamudPenumbraState.RequiresResumeAfterPluginLoad)
        {
            _dalamudPromptHandled = true;
            var enableOption = await ShowDalamudModalAsync();
            if (enableOption && _dalamudService.TryEnableResumeAfterPluginLoad(_dalamudCheck))
            {
                _dalamudCheck = new DalamudPenumbraCheck(DalamudPenumbraState.Ready, _dalamudCheck.ConfigPath);
                NotifyEnvironmentProperties();
            }
        }

        IsBusy = true;
        Stage = PatcherUiStage.Generating;
        LastSuccess = null;
        StatusText = "Generando";
        ProgressPercent = 5;
        ProgressText = "Preparando datos";
        _lastResult = null;
        NotifyResultProperties();

        IReadOnlyCollection<string>? categories = selected.Length == enabled.Count ? null : selected;
        Directory.CreateDirectory(OutputFolder);
        var outputName = $"FFXIVSpanish-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.pmp";
        var request = new PatchRequest
        {
            GamePath = GamePath,
            Categories = categories,
            CompatibilityMode = compatibilityMode,
            OutputPath = Path.Combine(OutputFolder, outputName),
            StagingPath = Path.Combine(Path.GetTempPath(), "ffxivsp-patcher-staging"),
            DebugLogging = _debugLogging,
            Meta = BuildPackageMeta(enabled),
        };

        var progress = new Progress<PipelineEvent>(HandlePipelineEvent);
        var pipeline = PatchPipeline.ForClient(new ListTranslationSource(_entries!));
        try
        {
            _lastResult = await Task.Run(() => pipeline.Run(request, progress));
        }
        catch (Exception exception)
        {
            Console.Add(Error($"Error inesperado: {exception.Message}"));
            _lastResult = new PatchResult(PatchOutcome.ValidationFailed, null, new PatchStatistics());
        }

        LastSuccess = _lastResult.Success;
        if (_lastResult.Success)
        {
            LastOutputName = outputName;
            ProgressPercent = 100;
            ProgressText = "Completado";
        }

        Stage = PatcherUiStage.Result;
        StatusText = _lastResult.Outcome == PatchOutcome.Ok
            ? "Completado"
            : _lastResult.Success ? "Completado con omisiones" : "Error";
        IsBusy = false;
        NotifyResultProperties();
    }

    private void HandlePipelineEvent(PipelineEvent pipelineEvent)
    {
        Console.Add(new ConsoleLine(pipelineEvent));
        switch (pipelineEvent.Component)
        {
            case PipelineComponent.Extractor:
                ProgressPercent = Math.Max(ProgressPercent, 25);
                ProgressText = "Leyendo instalación";
                break;
            case PipelineComponent.Patcher:
                ProgressPercent = Math.Max(ProgressPercent, 58);
                ProgressText = "Aplicando traducciones";
                break;
            case PipelineComponent.Packager:
                ProgressPercent = Math.Max(ProgressPercent, 82);
                ProgressText = "Empaquetando mod";
                break;
            case PipelineComponent.Verifier:
                ProgressPercent = Math.Max(ProgressPercent, 95);
                ProgressText = "Verificando integridad";
                break;
        }
    }

    private PackageMeta BuildPackageMeta(IReadOnlyList<CategoryViewModel> enabled)
    {
        var version = $"v{_buildInfo.PackageVersion}"
                      + (string.IsNullOrEmpty(InstalledGameVersion) ? "" : $"-{InstalledGameVersion}");
        var domains = string.Join(
            "\n",
            enabled.Where(category => category.IsSelected).Select(category => $"* {category.Label}"));
        var description = new PackageMeta().Description
                          + $"\n\nVersión del patcher: v{_buildInfo.PackageVersion}"
                          + $"\nVersión de FFXIV: {InstalledGameVersion ?? "desconocida"}"
                          + $"\n\nCategorías incluidas:\n{domains}";
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
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        GamePath = picked;
        if (GamePathDetector.IsValid(picked))
        {
            Console.Add(Info($"Ruta del juego: {picked}"));
        }
        else
        {
            Console.Add(Error(InvalidGamePathMessage(picked)));
        }
    }

    [RelayCommand]
    private void SelectAllCategories()
    {
        foreach (var category in Categories.Where(category => category.IsEnabled))
        {
            category.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNoCategories()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = false;
        }
    }

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedOpen = !IsAdvancedOpen;

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
        => await _shell.CopyToClipboardAsync(string.Join(Environment.NewLine, Console.Select(line => line.Text)));

    [RelayCommand]
    private void ShowMilestones() => IsMilestoneOpen = true;

    [RelayCommand]
    private void CloseMilestones() => IsMilestoneOpen = false;

    [RelayCommand]
    private async Task ReviewDalamudAsync()
    {
        if (_dalamudCheck.State != DalamudPenumbraState.RequiresResumeAfterPluginLoad)
        {
            return;
        }

        _dalamudPromptHandled = true;
        if (await ShowDalamudModalAsync()
            && _dalamudService.TryEnableResumeAfterPluginLoad(_dalamudCheck))
        {
            _dalamudCheck = new DalamudPenumbraCheck(DalamudPenumbraState.Ready, _dalamudCheck.ConfigPath);
            NotifyEnvironmentProperties();
        }
    }

    [RelayCommand]
    private void AcceptModal() => CloseModal(true);

    [RelayCommand]
    private void DismissModal() => CloseModal(false);

    private Task<bool> ShowVersionMismatchModalAsync()
        => ShowModalAsync(
            "La versión del juego no coincide con esta traducción",
            $"Este parcheador contiene traducciones preparadas para FFXIV {_recommendedGameVersion}, " +
            $"pero la instalación seleccionada es {InstalledGameVersion}.",
            "Se intentará generar un mod utilizando los archivos que tienes instalados. Las hojas, páginas y líneas " +
            "que no existan en esta versión se omitirán. El resultado puede contener menos traducciones, pero no se " +
            "modificarán los archivos originales del juego.",
            "Al finalizar se indicará exactamente qué se ha podido aplicar.",
            "Generar de todos modos",
            "Volver");

    private Task<bool> ShowDalamudModalAsync()
        => ShowModalAsync(
            "Haz que Penumbra termine de cargar antes de iniciar FFXIV",
            "El patcher ha detectado Dalamud y Penumbra, pero Dalamud no está esperando a que los plugins terminen " +
            "de cargar antes de continuar con el juego. La traducción puede no activarse a tiempo.",
            "¿Quieres corregirlo automáticamente? Se activará en Dalamud la opción " +
            "«Esperar a que los plugins se carguen antes de iniciar el juego».",
            "Solo cambiará esta opción. No se modificarán Penumbra ni los archivos del juego.",
            "Activar opción",
            "Ahora no");

    private Task<bool> ShowModalAsync(
        string title,
        string body,
        string explanation,
        string note,
        string primary,
        string secondary)
    {
        _modalCompletion?.TrySetResult(false);
        _modalCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ModalTitle = title;
        ModalBody = body;
        ModalExplanation = explanation;
        ModalNote = note;
        ModalPrimaryText = primary;
        ModalSecondaryText = secondary;
        IsModalOpen = true;
        return _modalCompletion.Task;
    }

    private void CloseModal(bool accepted)
    {
        IsModalOpen = false;
        var completion = _modalCompletion;
        _modalCompletion = null;
        completion?.TrySetResult(accepted);
    }

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

    private void OnCategoriesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null)
        {
            foreach (var category in args.OldItems.OfType<CategoryViewModel>())
            {
                category.PropertyChanged -= OnCategoryPropertyChanged;
            }
        }

        if (args.NewItems is not null)
        {
            foreach (var category in args.NewItems.OfType<CategoryViewModel>())
            {
                category.PropertyChanged += OnCategoryPropertyChanged;
            }
        }

        NotifyCategoryProperties();
    }

    private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CategoryViewModel.IsSelected))
        {
            NotifyCategoryProperties();
        }
    }

    private void NotifyCategoryProperties()
    {
        OnPropertyChanged(nameof(AvailableCategoryCount));
        OnPropertyChanged(nameof(SelectedCategoryCount));
        OnPropertyChanged(nameof(HasSelectedCategories));
        OnPropertyChanged(nameof(CategorySummary));
        OnPropertyChanged(nameof(ShowCategorySelectionError));
        GenerateModCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPreparationProperties()
    {
        OnPropertyChanged(nameof(SetupTitle));
        OnPropertyChanged(nameof(SetupLead));
        OnPropertyChanged(nameof(CorpusCheckTitle));
        OnPropertyChanged(nameof(CorpusCheckSubtitle));
        OnPropertyChanged(nameof(ShowCategorySelectionError));
    }

    private void NotifyEnvironmentProperties()
    {
        OnPropertyChanged(nameof(DalamudCheckTitle));
        OnPropertyChanged(nameof(DalamudCheckSubtitle));
        OnPropertyChanged(nameof(IsDalamudWarning));
        OnPropertyChanged(nameof(IsDalamudOk));
    }

    private void NotifyResultProperties()
    {
        OnPropertyChanged(nameof(IsResultSuccess));
        OnPropertyChanged(nameof(IsResultPartial));
        OnPropertyChanged(nameof(IsResultError));
        OnPropertyChanged(nameof(IsUsableResult));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultSubtitle));
        OnPropertyChanged(nameof(ResultApplied));
        OnPropertyChanged(nameof(ResultPages));
        OnPropertyChanged(nameof(ResultOmitted));
        OnPropertyChanged(nameof(ResultFailures));
    }

    private void Reject(string message)
    {
        LastSuccess = false;
        StatusText = "Error";
        Console.Add(Error(message));
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

    private static string InvalidGamePathMessage(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? "Selecciona la carpeta de instalación de FFXIV antes de generar el mod."
            : $"La ruta seleccionada no contiene datos válidos de FFXIV: {path}";

    private static ITranslationSource CreateDefaultTranslationSource()
    {
        var assembly = typeof(MainViewModel).Assembly;
        var useExternalTranslations = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute =>
                attribute.Key.Equals(ExternalTranslationsMetadataName, StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(attribute.Value, out var enabled)
                && enabled);
        return useExternalTranslations
            ? EmbeddedTranslationSource.FromFile(Path.Combine(AppContext.BaseDirectory, ExternalTranslationsFileName))
            : EmbeddedTranslationSource.FromAssemblyResource(assembly, TranslationResourceName);
    }

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
