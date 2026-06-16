using XivSpanish.GameData;
using XivSpanish.Packager;
using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Orchestrates a full run: load translations -> SeString gate -> resolve/group pages -> patch each
/// page (with duplicate-row broadcast) -> contamination guard -> package -> optional integrity
/// verification. Progress is reported as <see cref="PipelineEvent"/>s so the GUI can stream a
/// console. This is the orchestration ported from the upstream Packager's Program.cs Main; the
/// game-data and packaging primitives it calls stay vendored.
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

        void Conflict(string message) => Report(PipelineComponent.Patcher, message, PipelineLevel.Warning);

        Report(PipelineComponent.Pipeline, "Iniciando generación del mod...");

        // 1. Load the approved translation entries.
        var entries = _translations.Load();
        Report(PipelineComponent.Patcher, "Cargando traducciones (FFXIVSpanish)", PipelineLevel.Ok, entries.Count);

        var selection = TranslationCategories.BuildSelection(request.Categories);
        bool IsCandidate(TranslationEntry e)
            => Packageable(e, request.Status) is null && TranslationCategories.IsSelected(e, selection);

        // 2. Hard SeString gate over the build candidates (all offending rows listed; never stops first).
        var gate = ManifestSeStringGate.Check(entries.Where(IsCandidate));
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
                    Report(PipelineComponent.Patcher, violation.Describe(), PipelineLevel.Error);
                }

                Report(PipelineComponent.Pipeline,
                    $"SeString gate: {gate.Count} fila(s) con destino incompatible. Generación abortada.", PipelineLevel.Error);
                return PatchResult.Failure(PatchOutcome.SeStringGate);
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
        catch (DirectoryNotFoundException exception)
        {
            Report(PipelineComponent.Extractor, exception.Message, PipelineLevel.Error);
            return PatchResult.Failure(PatchOutcome.GameDataError);
        }

        using (backend)
        {
            Report(PipelineComponent.Extractor, "Archivos base verificados", PipelineLevel.Ok);

            // 4. Group candidates by EXD page path.
            var pages = new Dictionary<string, PagePatch>(StringComparer.OrdinalIgnoreCase);
            var skipped = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCandidate(entry))
                {
                    skipped++;
                    continue;
                }

                var key = entry.SourceKey!;
                var exdPath = backend.ResolveExdPath(key);
                if (exdPath is null)
                {
                    skipped++;
                    Report(PipelineComponent.Extractor,
                        $"omitido {entry.Id}: no se resuelve la ruta EXD de {key.Sheet}/{key.RowId}", PipelineLevel.Warning);
                    continue;
                }

                if (!pages.TryGetValue(exdPath, out var page))
                {
                    page = new PagePatch(key.Sheet);
                    pages[exdPath] = page;
                }

                page.Add(
                    key.RowId!.Value,
                    new StringReplacement(entry.Source, entry.Target, string.IsNullOrWhiteSpace(key.Field) ? null : key.Field),
                    Conflict);
            }

            if (pages.Count == 0)
            {
                Report(PipelineComponent.Pipeline, "No hay entradas empaquetables para la selección.", PipelineLevel.Warning);
                return PatchResult.Failure(PatchOutcome.NothingToPackage, skipped);
            }

            // 5. Broadcast table: approved target per sheet+field+source (ambiguous source -> null).
            var broadcast = BuildBroadcast(entries, request.Status, selection);

            // 6. Patch each page into the staging tree.
            var writer = new PackageWriter(request.StagingPath);
            var totalApplied = 0;
            var totalMissed = 0;
            var missedAbsentSource = 0;

            foreach (var (exdPath, page) in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var layout = backend.BaseSource.ReadStringLayout(page.Sheet);
                var raw = backend.BaseSource.ReadBaseExd(exdPath);
                if (layout is null || raw is null)
                {
                    Report(PipelineComponent.Patcher,
                        $"omitida página {exdPath}: falta layout EXH o bytes EXD ({page.Sheet})", PipelineLevel.Warning);
                    continue;
                }

                if (layout.Value.Variant == 2)
                {
                    Report(PipelineComponent.Patcher,
                        $"omitida página {exdPath}: {page.Sheet} es subrow variant 2 (no soportado)", PipelineLevel.Warning);
                    continue;
                }

                var fieldNames = backend.BaseSource.ResolveFieldNames(page.Sheet, layout.Value.StringColumnOffsets.Count);

                // Broadcast approved targets to duplicate base rows the manifest does not list.
                if (broadcast.TryGetValue(page.Sheet, out var sheetTargets))
                {
                    foreach (var (rowId, column) in ExdRowReader.Read(
                                 raw, layout.Value.FixedDataSize, layout.Value.StringColumnOffsets))
                    {
                        if (column.HasPayload)
                        {
                            continue;
                        }

                        var field = column.ColumnOrdinal < fieldNames.Count ? fieldNames[column.ColumnOrdinal] : string.Empty;
                        string? target = null;
                        if (sheetTargets.TryGetValue(field, out var bySource) && bySource.TryGetValue(column.Source, out var byFieldTarget))
                        {
                            target = byFieldTarget;
                        }
                        else if (sheetTargets.TryGetValue(string.Empty, out var anyField) && anyField.TryGetValue(column.Source, out var anyTarget))
                        {
                            target = anyTarget;
                            field = string.Empty;
                        }

                        if (target is not null)
                        {
                            page.Add(rowId, new StringReplacement(column.Source, target, field.Length == 0 ? null : field), Conflict);
                        }
                    }
                }

                ExdPatchResult result;
                try
                {
                    result = ExdPatcher.Patch(
                        raw, layout.Value.FixedDataSize, layout.Value.StringColumnOffsets, page.ToReplacements(), fieldNames);
                }
                catch (InvalidDataException exception)
                {
                    Report(PipelineComponent.Patcher, $"omitida página {exdPath}: {exception.Message}", PipelineLevel.Warning);
                    continue;
                }

                totalApplied += result.Applied;
                totalMissed += result.Missed.Count;
                foreach (var miss in result.Missed)
                {
                    if (miss.Reason == ContaminationGuard.AbsentSourceReason)
                    {
                        missedAbsentSource++;
                    }
                }

                writer.AddPatchedExd(exdPath, result.Bytes);
                Report(PipelineComponent.Patcher, page.Sheet,
                    result.Missed.Count == 0 ? PipelineLevel.Ok : PipelineLevel.Warning, result.Applied);
            }

            if (writer.FileCount == 0)
            {
                Report(PipelineComponent.Pipeline, "Ninguna página EXD fue parcheada.", PipelineLevel.Warning);
                return PatchResult.Failure(PatchOutcome.NothingToPackage, skipped);
            }

            // 7. Contamination guard: a base that no longer matches English sources is likely modded.
            var guard = ContaminationGuard.Evaluate(totalApplied, missedAbsentSource, request.MinMatchRate, minVolume: 50);
            if (guard.Contaminated)
            {
                Report(PipelineComponent.Pipeline,
                    $"Base EXD contaminada o ya traducida (match {guard.MatchRate:P1} < umbral {request.MinMatchRate:P1}). " +
                    "Usa una instalación limpia del juego.", PipelineLevel.Error);
                return PatchResult.Failure(PatchOutcome.Contaminated, skipped);
            }

            // 8. Write manifests and zip the .pmp.
            Report(PipelineComponent.Packager, "Generando .pmp...");
            var output = writer.Package(request.Meta, request.OutputPath);
            Report(PipelineComponent.Packager, "Comprimiendo y empaquetando archivos", PipelineLevel.Ok, writer.FileCount);

            // 9. Optional integrity verification.
            if (request.VerifyIntegrity)
            {
                var problems = _verifier.Verify(output, writer.DeclaredFiles);
                if (problems.Count > 0)
                {
                    foreach (var problem in problems)
                    {
                        Report(PipelineComponent.Verifier, problem, PipelineLevel.Error);
                    }

                    return PatchResult.Failure(PatchOutcome.ValidationFailed, skipped);
                }

                Report(PipelineComponent.Verifier, "Integridad verificada", PipelineLevel.Ok);
            }

            var outcome = totalMissed > 0 ? PatchOutcome.PackagedWithMisses : PatchOutcome.Ok;
            Report(PipelineComponent.Pipeline, "Proceso completado correctamente.", PipelineLevel.Ok);
            return new PatchResult(outcome, output, writer.FileCount, totalApplied, totalMissed, skipped);
        }
    }

    /// <summary>Reason an entry is not packageable, or null when it is.</summary>
    private static string? Packageable(TranslationEntry entry, string requiredStatus)
    {
        if (!string.Equals(entry.Status, requiredStatus, StringComparison.OrdinalIgnoreCase))
        {
            return $"status '{entry.Status}' != '{requiredStatus}'";
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

    private static Dictionary<string, Dictionary<string, Dictionary<string, string?>>> BuildBroadcast(
        IReadOnlyList<TranslationEntry> entries, string status, IReadOnlySet<string>? selection)
    {
        var broadcast = new Dictionary<string, Dictionary<string, Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (Packageable(entry, status) is not null || !TranslationCategories.IsSelected(entry, selection))
            {
                continue;
            }

            var key = entry.SourceKey!;
            var field = string.IsNullOrWhiteSpace(key.Field) ? string.Empty : key.Field;
            if (!broadcast.TryGetValue(key.Sheet, out var byField))
            {
                byField = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
                broadcast[key.Sheet] = byField;
            }

            if (!byField.TryGetValue(field, out var bySource))
            {
                bySource = new Dictionary<string, string?>(StringComparer.Ordinal);
                byField[field] = bySource;
            }

            if (!bySource.TryGetValue(entry.Source, out var existing))
            {
                bySource[entry.Source] = entry.Target;
            }
            else if (existing is not null && existing != entry.Target)
            {
                bySource[entry.Source] = null; // ambiguous: disable broadcast for this source
            }
        }

        return broadcast;
    }

    /// <summary>Replacements grouped for one EXD page, deduped per (field, source).</summary>
    private sealed class PagePatch(string sheet)
    {
        private readonly Dictionary<uint, List<StringReplacement>> _rows = new();

        public string Sheet { get; } = sheet;

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
