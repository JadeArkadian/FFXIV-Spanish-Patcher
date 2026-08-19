using System.Security.Cryptography;
using XivSpanish.GameData;
using XivSpanish.Packager;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Orchestrates a full run: load translations -> SeString gate -> resolve/group pages -> patch each
/// page (with duplicate-row broadcast and safe field aliases) -> contamination guard -> package
/// -> mandatory integrity verification -> atomic promotion. Progress is reported as
/// <see cref="PipelineEvent"/>s so
/// the GUI can stream a console. This is the orchestration ported from the upstream Packager's
/// Program.cs Main; the game-data and packaging primitives it calls stay vendored.
/// </summary>
public sealed class PatchPipeline
{
    private readonly ITranslationSource _translations;
    private readonly IPatchBackendFactory _backendFactory;
    private readonly IIntegrityVerifier _verifier;

    public PatchPipeline(
        ITranslationSource translations,
        IPatchBackendFactory backendFactory,
        IIntegrityVerifier? verifier = null)
    {
        _translations = translations;
        _backendFactory = backendFactory;
        _verifier = verifier ?? new IntegrityVerifier();
    }

    /// <summary>Wires the production client backend over a translation source.</summary>
    public static PatchPipeline ForClient(ITranslationSource translations)
        => new(translations, new ClientPatchBackendFactory());

    public PatchResult Run(
        PatchRequest request,
        IProgress<PipelineEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        void Report(PipelineComponent component, string message, PipelineLevel level = PipelineLevel.Info, int? count = null)
            => progress?.Report(new PipelineEvent(component, message, level, count));

        void Debug(string message, int? count = null)
        {
            if (request.DebugLogging)
            {
                Report(PipelineComponent.Patcher, message, PipelineLevel.Debug, count);
            }
        }

        void Conflict(string message) => Report(PipelineComponent.Patcher, message, PipelineLevel.Warning);

        Report(PipelineComponent.Pipeline, "Iniciando generación del mod...");

        // 1. Load the approved translation entries.
        IReadOnlyList<TranslationEntry> entries;
        try
        {
            entries = _translations.Load();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            Report(PipelineComponent.Patcher,
                $"No se pudieron cargar las traducciones: {exception.Message}", PipelineLevel.Error);
            return PatchResult.Failure(PatchOutcome.ValidationFailed);
        }

        Report(PipelineComponent.Patcher, "Cargando traducciones (FFXIVSpanish)", PipelineLevel.Ok, entries.Count);

        var selection = TranslationCategories.BuildSelection(request.Categories);
        var unsafeSeStringEntries = new HashSet<TranslationEntry>();
        bool IsPackageableForSelection(TranslationEntry e)
            => Packageable(e, request.Statuses) is null && TranslationCategories.IsSelected(e, selection);
        bool IsCandidate(TranslationEntry e)
            => IsPackageableForSelection(e) && !unsafeSeStringEntries.Contains(e);
        var candidateEntries = entries.Count(IsPackageableForSelection);

        var appliedWrites = 0;
        var rowMisses = 0;
        var missingSheetEntries = 0;
        var missingPageEntries = 0;
        var unresolvedRows = 0;
        var unsupportedPages = 0;
        var unsupportedPageEntries = 0;
        var skippedPages = 0;
        var missingSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PatchStatistics Statistics(int patchedPages = 0) => new(
            CandidateEntries: candidateEntries,
            AppliedWrites: appliedWrites,
            RowMisses: rowMisses,
            MissingSheets: missingSheets.Count,
            MissingSheetEntries: missingSheetEntries,
            MissingPages: missingPages.Count,
            MissingPageEntries: missingPageEntries,
            UnresolvedRows: unresolvedRows,
            UnsafeSeStringEntries: unsafeSeStringEntries.Count,
            UnsupportedPages: unsupportedPages,
            UnsupportedPageEntries: unsupportedPageEntries,
            PatchedPages: patchedPages,
            SkippedPages: skippedPages);

        // 2. SeString gate over the build candidates. Unsafe rows are skipped by default; a few bad
        // corpus rows must not abort an otherwise valid package.
        var gate = ManifestSeStringGate.Check(entries.Where(IsPackageableForSelection));
        if (gate.Count > 0)
        {
            if (request.ForceSeString)
            {
                foreach (var violation in gate)
                {
                    Report(PipelineComponent.Patcher, violation.DescribeOverride(), PipelineLevel.Warning);
                }

                Report(PipelineComponent.Patcher,
                    $"{gate.Count} fila(s) empaquetadas pese a violaciones SeString.", PipelineLevel.Warning);
            }
            else
            {
                foreach (var violation in gate)
                {
                    unsafeSeStringEntries.Add(violation.Entry);
                    Report(
                        PipelineComponent.Patcher,
                        $"omitida fila insegura: {violation.Describe()}",
                        PipelineLevel.Warning);
                }

                Report(PipelineComponent.Pipeline,
                    $"SeString gate: {gate.Count} fila(s) omitida(s) por SeString incompatible. Se continúa con el resto.", PipelineLevel.Warning);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Open the game-data backend (live client or vanilla snapshot).
        Report(PipelineComponent.Extractor, "Verificando archivos base del juego...");
        IPatchBackend backend;
        try
        {
            backend = _backendFactory.Open(request);
        }
        catch (Exception exception) when (exception is DirectoryNotFoundException
                                          or FileNotFoundException
                                          or UnauthorizedAccessException
                                          or IOException
                                          or InvalidDataException)
        {
            Report(PipelineComponent.Extractor, exception.Message, PipelineLevel.Error);
            return PatchResult.Failure(PatchOutcome.GameDataError, Statistics());
        }

        using (backend)
        {
            Report(PipelineComponent.Extractor, "Archivos base verificados", PipelineLevel.Ok);

            // 4. Group candidates by EXD page path.
            var pages = new Dictionary<string, PagePatch>(StringComparer.OrdinalIgnoreCase);
            var unresolvedBySheet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var missingBySheet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCandidate(entry))
                {
                    continue;
                }

                var key = entry.SourceKey!;
                var resolution = backend.ResolveExd(key);
                if (resolution.Kind == ExdResolutionKind.MissingSheet)
                {
                    missingSheets.Add(key.Sheet);
                    missingSheetEntries++;
                    Increment(missingBySheet, key.Sheet);
                    continue;
                }

                if (resolution.Kind != ExdResolutionKind.Resolved || resolution.Path is null)
                {
                    unresolvedRows++;
                    Increment(unresolvedBySheet, key.Sheet);
                    continue;
                }

                var exdPath = resolution.Path;
                if (!pages.TryGetValue(exdPath, out var page))
                {
                    page = new PagePatch(key.Sheet);
                    pages[exdPath] = page;
                }

                page.AddManifest(
                    key.RowId!.Value,
                    new StringReplacement(entry.Source, entry.Target, string.IsNullOrWhiteSpace(key.Field) ? null : key.Field),
                    Conflict);
            }

            foreach (var (sheet, count) in missingBySheet.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                Report(
                    PipelineComponent.Extractor,
                    $"omitida hoja {sheet}: no existe en esta versión ({count} entrada(s))",
                    PipelineLevel.Warning,
                    count);
            }

            foreach (var (sheet, count) in unresolvedBySheet.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                Report(
                    PipelineComponent.Extractor,
                    $"omitidas {count} fila(s) de {sheet}: no pertenecen a ninguna página de esta versión",
                    PipelineLevel.Warning,
                    count);
            }

            if (pages.Count == 0)
            {
                Report(PipelineComponent.Pipeline, "No hay entradas empaquetables para la selección.", PipelineLevel.Warning);
                return PatchResult.Failure(PatchOutcome.NothingToPackage, Statistics());
            }

            // 5. Broadcast table: approved target per sheet+field+source (ambiguous source -> null).
            var broadcast = BuildBroadcastCatalog(
                entries.Where(e => !unsafeSeStringEntries.Contains(e)).ToList(),
                request.Statuses,
                selection);

            // 6. Patch each page into an isolated per-run staging tree.
            var runStaging = Path.Combine(request.StagingPath, Guid.NewGuid().ToString("N"));
            var temporaryOutput = string.Empty;
            var missedAbsentSource = 0;
            var gameReadErrors = 0;
            try
            {
                temporaryOutput = SiblingTemporaryPath(request.OutputPath);
                var writer = new PackageWriter(runStaging);
                foreach (var (exdPath, page) in pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ExdLayout? layout;
                    byte[]? raw;
                    try
                    {
                        layout = backend.BaseSource.ReadStringLayout(page.Sheet);
                        raw = layout is null ? null : backend.BaseSource.ReadBaseExd(exdPath);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException)
                    {
                        gameReadErrors++;
                        missingPages.Add(exdPath);
                        missingPageEntries += page.ManifestEntryCount;
                        skippedPages++;
                        Report(
                            PipelineComponent.Patcher,
                            $"omitida página {exdPath}: no se pudo leer ({exception.Message})",
                            PipelineLevel.Warning,
                            page.ManifestEntryCount);
                        continue;
                    }

                    if (layout is null)
                    {
                        missingSheets.Add(page.Sheet);
                        missingSheetEntries += page.ManifestEntryCount;
                        skippedPages++;
                        Report(
                            PipelineComponent.Patcher,
                            $"omitida hoja {page.Sheet}: falta su layout EXH ({page.ManifestEntryCount} entrada(s))",
                            PipelineLevel.Warning,
                            page.ManifestEntryCount);
                        continue;
                    }

                    if (raw is null)
                    {
                        missingPages.Add(exdPath);
                        missingPageEntries += page.ManifestEntryCount;
                        skippedPages++;
                        Report(
                            PipelineComponent.Patcher,
                            $"omitida página {exdPath}: no existe en esta versión ({page.ManifestEntryCount} entrada(s))",
                            PipelineLevel.Warning,
                            page.ManifestEntryCount);
                        continue;
                    }

                    if (layout.Value.Variant == 2)
                    {
                        unsupportedPages++;
                        unsupportedPageEntries += page.ManifestEntryCount;
                        skippedPages++;
                        Report(
                            PipelineComponent.Patcher,
                            $"omitida página {exdPath}: {page.Sheet} es subrow variant 2 (no soportado)",
                            PipelineLevel.Warning,
                            page.ManifestEntryCount);
                        continue;
                    }

                    IReadOnlyList<string> fieldNames;
                    try
                    {
                        fieldNames = backend.BaseSource.ResolveFieldNames(
                            page.Sheet,
                            layout.Value.StringColumnOffsets.Count);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException)
                    {
                        gameReadErrors++;
                        missingPages.Add(exdPath);
                        missingPageEntries += page.ManifestEntryCount;
                        skippedPages++;
                        Report(
                            PipelineComponent.Patcher,
                            $"omitida página {exdPath}: no se pudo leer su esquema ({exception.Message})",
                            PipelineLevel.Warning,
                            page.ManifestEntryCount);
                        continue;
                    }

                    // Broadcast approved targets to duplicate base rows the manifest does not list.
                    // Payload-bearing strings require the raw signature of an explicit approved row.
                    IReadOnlyList<BroadcastColumn> broadcastColumns;
                    try
                    {
                        broadcastColumns = ReadBroadcastColumns(
                            raw,
                            layout.Value.FixedDataSize,
                            layout.Value.StringColumnOffsets,
                            fieldNames);
                    }
                    catch (InvalidDataException exception)
                    {
                        unsupportedPages++;
                        unsupportedPageEntries += page.ManifestEntryCount;
                        skippedPages++;
                        Report(
                            PipelineComponent.Patcher,
                            $"omitida página {exdPath}: {exception.Message}",
                            PipelineLevel.Warning,
                            page.ManifestEntryCount);
                        continue;
                    }
                    var broadcasted = 0;
                    var payloadBroadcasted = 0;
                    foreach (var column in broadcastColumns)
                    {
                        var decision = BroadcastPlanner.Decide(broadcast, page.Sheet, column);
                        if (decision is null)
                        {
                            continue;
                        }

                        if (page.Add(
                                column.RowId,
                                new StringReplacement(column.Source, decision.Target, decision.ReplacementField),
                                Conflict))
                        {
                            broadcasted++;
                        }
                    }

                    // Payload-bearing duplicates cannot be matched by tokenized source (the corpus is
                    // run-aware, the base re-tokenization is flat), so they are broadcast by raw byte
                    // identity: every base row byte-identical to an explicitly-translated payload row
                    // receives that row's reviewed replacement. This closes the gap that left duplicate
                    // payload rows (e.g. Addon 196, a byte-identical twin of the translated row 111)
                    // showing vanilla English.
                    foreach (var sibling in BroadcastPlanner.PlanPayloadSiblings(
                                 broadcastColumns,
                                 page.ToReplacements()))
                    {
                        if (page.Add(sibling.RowId, sibling.Replacement, Conflict))
                        {
                            broadcasted++;
                            payloadBroadcasted++;
                        }
                    }

                    if (broadcasted > 0)
                    {
                        Debug(
                            $"broadcast {exdPath}: +{broadcasted} duplicados ({payloadBroadcasted} payload-safe)",
                            broadcasted);
                    }

                    var fieldAliasBroadcasted = 0;
                    foreach (var decision in FieldAliasPlanner.Decide(
                                 page.Sheet,
                                 broadcastColumns,
                                 page.ToReplacements()))
                    {
                        if (page.Add(
                                decision.RowId,
                                new StringReplacement(decision.Source, decision.Target, decision.ReplacementField),
                                Conflict))
                        {
                            fieldAliasBroadcasted++;
                        }
                    }

                    if (fieldAliasBroadcasted > 0)
                    {
                        Debug(
                            $"field-alias {exdPath}: +{fieldAliasBroadcasted} alias de campo",
                            fieldAliasBroadcasted);
                    }

                    ExdPatchResult result;
                    try
                    {
                        result = ExdPatcher.Patch(
                            raw,
                            layout.Value.FixedDataSize,
                            layout.Value.StringColumnOffsets,
                            page.ToReplacements(),
                            fieldNames);
                    }
                    catch (InvalidDataException exception)
                    {
                        unsupportedPages++;
                        unsupportedPageEntries += page.ManifestEntryCount;
                        skippedPages++;
                        Report(
                            PipelineComponent.Patcher,
                            $"omitida página {exdPath}: {exception.Message}",
                            PipelineLevel.Warning,
                            page.ManifestEntryCount);
                        continue;
                    }

                    appliedWrites += result.Applied;
                    rowMisses += result.Missed.Count;
                    foreach (var miss in result.Missed)
                    {
                        if (miss.Reason == ContaminationGuard.AbsentSourceReason)
                        {
                            missedAbsentSource++;
                        }

                        Debug($"miss {page.Sheet}/{miss.RowId}: {miss.Reason} | source: {Preview(miss.Source)}");
                    }

                    if (result.Applied > 0)
                    {
                        writer.AddPatchedExd(exdPath, result.Bytes);
                    }
                    else
                    {
                        skippedPages++;
                    }

                    Report(
                        PipelineComponent.Patcher,
                        PageResultMessage(page.Sheet, result.Missed),
                        result.Missed.Count == 0 ? PipelineLevel.Ok : PipelineLevel.Warning,
                        result.Applied);
                }

                if (appliedWrites == 0 || writer.FileCount == 0)
                {
                    if (gameReadErrors > 0 && gameReadErrors == pages.Count)
                    {
                        Report(PipelineComponent.Extractor,
                            "No se pudo leer ninguna página del juego; comprueba la instalación y los permisos.",
                            PipelineLevel.Error);
                        return PatchResult.Failure(PatchOutcome.GameDataError, Statistics(writer.FileCount));
                    }

                    Report(PipelineComponent.Pipeline,
                        "No se aplicó ninguna traducción; no se generará un paquete vacío.",
                        PipelineLevel.Error);
                    return PatchResult.Failure(PatchOutcome.NothingToPackage, Statistics(writer.FileCount));
                }

                // 7. Guard over readable rows only. Missing sheets/pages never lower this rate.
                var guard = ContaminationGuard.Evaluate(
                    appliedWrites,
                    missedAbsentSource,
                    request.MinMatchRate,
                    minVolume: 50);
                if (guard.Contaminated)
                {
                    if (request.CompatibilityMode == PatchCompatibilityMode.Strict)
                    {
                        Report(PipelineComponent.Pipeline,
                            $"Base EXD contaminada o incompatible (coincidencia {guard.MatchRate:P1} < umbral {request.MinMatchRate:P1}). " +
                            "Usa una instalación limpia del juego o confirma el modo de compatibilidad desde la aplicación.",
                            PipelineLevel.Error);
                        return PatchResult.Failure(PatchOutcome.Contaminated, Statistics(writer.FileCount));
                    }

                    Report(PipelineComponent.Pipeline,
                        $"Compatibilidad best effort: coincidencia baja ({guard.MatchRate:P1}); se conservarán las páginas válidas.",
                        PipelineLevel.Warning);
                }

                // 8. Build beside the destination, verify every time, then promote atomically.
                Report(PipelineComponent.Packager, "Generando .pmp temporal...");
                var output = writer.Package(request.Meta, temporaryOutput);
                Report(PipelineComponent.Packager,
                    "Comprimiendo y empaquetando archivos", PipelineLevel.Ok, writer.FileCount);

                IReadOnlyList<string> problems;
                try
                {
                    problems = _verifier.Verify(output, writer.DeclaredFiles);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    problems = [$"No se pudo verificar el paquete: {exception.Message}"];
                }

                if (problems.Count > 0)
                {
                    foreach (var problem in problems)
                    {
                        Report(PipelineComponent.Verifier, problem, PipelineLevel.Error);
                    }

                    Report(PipelineComponent.Pipeline,
                        "La integridad falló. El paquete anterior, si existía, se conserva.",
                        PipelineLevel.Error);
                    return PatchResult.Failure(PatchOutcome.ValidationFailed, Statistics(writer.FileCount));
                }

                Report(PipelineComponent.Verifier, "Integridad verificada", PipelineLevel.Ok);
                PromoteVerifiedOutput(output, request.OutputPath);
                Report(PipelineComponent.Packager, "Paquete publicado en su destino", PipelineLevel.Ok);

                var statistics = Statistics(writer.FileCount);
                var outcome = statistics.HasOmissions ? PatchOutcome.PackagedWithMisses : PatchOutcome.Ok;
                Report(PipelineComponent.Pipeline, CoverageMessage(statistics),
                    outcome == PatchOutcome.Ok ? PipelineLevel.Ok : PipelineLevel.Warning);
                Report(PipelineComponent.Pipeline,
                    outcome == PatchOutcome.Ok
                        ? "Proceso completado correctamente."
                        : "Proceso completado con omisiones; el paquete verificado es utilizable.",
                    outcome == PatchOutcome.Ok ? PipelineLevel.Ok : PipelineLevel.Warning);
                return new PatchResult(outcome, request.OutputPath, statistics);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Report(PipelineComponent.Packager,
                    $"No se pudo escribir el paquete: {exception.Message}", PipelineLevel.Error);
                return PatchResult.Failure(PatchOutcome.OutputError, Statistics());
            }
            finally
            {
                if (temporaryOutput.Length > 0)
                {
                    DeleteFileBestEffort(temporaryOutput);
                }

                DeleteDirectoryBestEffort(runStaging);
            }
        }
    }

    /// <summary>Reason an entry is not packageable, or null when it is.</summary>
    private static string? Packageable(TranslationEntry entry, IReadOnlySet<string> statuses)
    {
        if (!PackageableStatus.IsPackageable(entry, statuses))
        {
            return $"status '{entry.Status}' not in [{string.Join(", ", statuses)}]";
        }

        if (string.IsNullOrEmpty(entry.Target))
        {
            return "empty target";
        }

        if (entry.SourceKey is null || string.IsNullOrWhiteSpace(entry.SourceKey.Sheet) || !entry.SourceKey.RowId.HasValue)
        {
            return "incomplete source key";
        }

        // An empty source with a non-empty target is a valid write-at-offset entry.
        return null;
    }

    private static BroadcastCatalog BuildBroadcastCatalog(
        IReadOnlyList<TranslationEntry> entries, IReadOnlySet<string> statuses, IReadOnlySet<string>? selection)
    {
        var broadcast = new BroadcastCatalog();
        foreach (var entry in entries)
        {
            if (Packageable(entry, statuses) is not null || !TranslationCategories.IsSelected(entry, selection))
            {
                continue;
            }

            broadcast.Add(entry);
        }

        return broadcast;
    }

    private static IReadOnlyList<BroadcastColumn> ReadBroadcastColumns(
        byte[] raw,
        int fixedDataSize,
        IReadOnlyList<int> stringColumnOffsets,
        IReadOnlyList<string> fieldNames)
    {
        var columns = new List<BroadcastColumn>();
        foreach (var (rowId, ordinal, rawString) in ExdRowReader.ReadRawStrings(raw, fixedDataSize, stringColumnOffsets))
        {
            var source = SeStringTokenizer.TokenizeRawText(rawString);
            if (string.IsNullOrEmpty(source))
            {
                continue;
            }

            var field = ordinal < fieldNames.Count ? fieldNames[ordinal] : string.Empty;
            columns.Add(new BroadcastColumn(
                rowId,
                field,
                source,
                SeStringCompatibilityValidator.HasPayloads(source),
                Convert.ToHexString(SHA256.HashData(rawString))));
        }

        return columns;
    }

    private static void Increment(Dictionary<string, int> counts, string key)
        => counts[key] = counts.GetValueOrDefault(key) + 1;

    private static string SiblingTemporaryPath(string outputPath)
    {
        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput)
            ?? throw new IOException($"No se puede determinar el directorio de salida de {outputPath}.");
        Directory.CreateDirectory(directory);
        return Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void PromoteVerifiedOutput(string temporaryPath, string outputPath)
    {
        var fullOutput = Path.GetFullPath(outputPath);
        if (File.Exists(fullOutput))
        {
            File.Replace(temporaryPath, fullOutput, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, fullOutput);
        }
    }

    private static void DeleteFileBestEffort(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup must not hide the real pipeline result.
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Cleanup must not hide the real pipeline result.
        }
    }

    private static string CoverageMessage(PatchStatistics statistics)
        => $"Cobertura: {statistics.AppliedWrites} escritura(s), {statistics.RowMisses} miss(es), " +
           $"{statistics.MissingSheets} hoja(s) ausente(s), {statistics.MissingPages} página(s) ausente(s), " +
           $"{statistics.UnresolvedRows} fila(s) fuera de versión, {statistics.UnsupportedPageEntries} entrada(s) " +
           $"en páginas no soportadas y {statistics.UnsafeSeStringEntries} fila(s) SeString omitida(s).";

    /// <summary>Single-line, length-capped source preview for diagnostic logs.</summary>
    private static string Preview(string source)
    {
        var flat = source.Replace("\r", " ").Replace("\n", " ");
        return flat.Length <= 160 ? flat : flat[..160] + "…";
    }

    private static string PageResultMessage(string sheet, IReadOnlyList<MissedReplacement> missed)
    {
        if (missed.Count == 0)
        {
            return sheet;
        }

        var distinctRowIds = missed
            .Select(miss => miss.RowId)
            .Distinct()
            .OrderBy(rowId => rowId)
            .ToArray();
        var visibleRowIds = string.Join(", ", distinctRowIds.Take(20));
        var remainder = distinctRowIds.Length > 20
            ? $" … y {distinctRowIds.Length - 20} más"
            : string.Empty;
        return $"{sheet}: {missed.Count} miss(es), rowId(s): {visibleRowIds}{remainder}";
    }

    /// <summary>Replacements grouped for one EXD page, deduped per (field, source).</summary>
    private sealed class PagePatch(string sheet)
    {
        private readonly Dictionary<uint, List<StringReplacement>> _rows = new();

        public string Sheet { get; } = sheet;
        public int ManifestEntryCount { get; private set; }

        public bool AddManifest(uint rowId, StringReplacement replacement, Action<string>? onConflict = null)
        {
            ManifestEntryCount++;
            return Add(rowId, replacement, onConflict);
        }

        public bool Add(uint rowId, StringReplacement replacement, Action<string>? onConflict = null)
        {
            if (!_rows.TryGetValue(rowId, out var list))
            {
                list = [];
                _rows[rowId] = list;
            }

            var existing = list.FirstOrDefault(item =>
                item.Source == replacement.Source
                && string.Equals(item.Field, replacement.Field, StringComparison.Ordinal));
            if (existing is null)
            {
                list.Add(replacement);
                return true;
            }

            if (existing.Target != replacement.Target)
            {
                onConflict?.Invoke(
                    $"conflicto fila {rowId}: fuente '{replacement.Source}' (campo '{replacement.Field}') con varios destinos, se mantiene '{existing.Target}'");
            }

            return false;
        }

        public IReadOnlyDictionary<uint, IReadOnlyList<StringReplacement>> ToReplacements()
            => _rows.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<StringReplacement>)pair.Value);
    }
}
