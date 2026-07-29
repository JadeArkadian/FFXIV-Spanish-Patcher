using System.IO.Compression;
using System.Text;
using FFXIVSpanishPatcher.Pipeline;
using XivSpanish.GameData;
using XivSpanish.Translation;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

/// <summary>
/// End-to-end pipeline test against a synthetic EXD: patches real binary EXD bytes, broadcasts a
/// translation to a duplicate row, fills an empty-offset column, packages a <c>.pmp</c> and verifies
/// its structure — all without a game install or any versioned <c>.exd</c>.
/// </summary>
public sealed class PatchPipelineTests : IDisposable
{
    private const string ExdPath = "exd/addon_0_en.exd";
    private const string ItemExdPath = "exd/item_9500_en.exd";
    private readonly string _temp;

    public PatchPipelineTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "ffxivsp-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private static FakeExdSource BuildSource()
    {
        var exd = SyntheticExd.BuildExd(
        [
            (1u, "Independent Arms Mender"),     // plain content replacement
            (2u, string.Empty),                  // empty column -> write-at-offset
            (262u, "Healing Magic Potency"),     // listed in the manifest
            (3256u, "Healing Magic Potency"),    // duplicate, reached only by broadcast
        ]);

        return new FakeExdSource()
            .AddPage(ExdPath, exd)
            .AddLayout("Addon", new ExdLayout(4, [0], 1));
    }

    private static FakeExdSource BuildItemSource()
    {
        var exd = SyntheticExd.BuildExd(
        [
            (9553u, ["Shiva's Diamond Bow", "Shiva's Diamond Bows", "Shiva's Diamond Bow"]),
        ], fixedSize: 12);

        return new FakeExdSource()
            .AddPage(ItemExdPath, exd)
            .AddLayout("Item", new ExdLayout(12, [0, 4, 8], 1))
            .AddFieldNames("Item", "Singular", "Plural", "Name");
    }

    private static TranslationEntry Approved(uint rowId, string source, string target)
        => Approved("Addon", rowId, string.Empty, ExdPath, source, target);

    private static TranslationEntry Approved(
        string sheet,
        uint rowId,
        string field,
        string exdPath,
        string source,
        string target)
        => new()
        {
            Source = source,
            Target = target,
            Status = TranslationEntryStatus.Approved,
            SourceKey = new TranslationSourceKey { Sheet = sheet, RowId = rowId, Field = field, ExdPath = exdPath },
        };

    private static IReadOnlyList<TranslationEntry> ApprovedManifest() =>
    [
        Approved(1u, "Independent Arms Mender", "Armero independiente"),
        Approved(2u, string.Empty, "Texto generado"),
        Approved(262u, "Healing Magic Potency", "Potencia de magia curativa"),
    ];

    private PatchRequest Request(
        IReadOnlyCollection<string>? categories = null,
        bool debugLogging = false,
        PatchCompatibilityMode compatibilityMode = PatchCompatibilityMode.Strict) => new()
        {
            OutputPath = Path.Combine(_temp, "out.pmp"),
            StagingPath = Path.Combine(_temp, "staging"),
            Categories = categories,
            DebugLogging = debugLogging,
            CompatibilityMode = compatibilityMode,
        };

    [Fact]
    public void Run_PatchesSyntheticExd_AndProducesValidPmp()
    {
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new FakePatchBackendFactory(BuildSource()));
        var events = new List<PipelineEvent>();

        var result = pipeline.Run(
            Request(),
            new SyncProgress<PipelineEvent>(events.Add),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.Ok, result.Outcome);
        Assert.Equal(1, result.Pages);
        Assert.Equal(0, result.Missed);
        // row 1 (content) + row 2 (empty-offset write) + row 262 (content) + row 3256 (broadcast) = 4
        Assert.Equal(4, result.Applied);
        Assert.True(File.Exists(result.OutputPath));

        // The package is a valid Penumbra mod: manifests + the redirected EXD file.
        using var archive = ZipFile.OpenRead(result.OutputPath!);
        var names = archive.Entries.Select(e => e.FullName).ToHashSet();
        Assert.Contains("meta.json", names);
        Assert.Contains("default_mod.json", names);
        Assert.Contains("files/exd/addon_0_en.exd", names);

        // The patched EXD carries the Spanish targets and no longer the English source.
        var patched = ReadEntryText(archive, "files/exd/addon_0_en.exd");
        Assert.Contains("Armero independiente", patched);
        Assert.Contains("Texto generado", patched);
        Assert.Contains("Potencia de magia curativa", patched);
        Assert.DoesNotContain("Independent Arms Mender", patched);

        // Verification is mandatory and reports success before the output is promoted.
        Assert.Contains(events, e => e.Component == PipelineComponent.Verifier && e.Level == PipelineLevel.Ok);
        Assert.DoesNotContain(events, e => e.Level == PipelineLevel.Debug);
    }

    [Fact]
    public void Run_PlayerGenderMacro_PackagesCompiledConditional()
    {
        const string sheet = "quest/045/ChrHdb811_04542";
        const string exdPath = "exd/quest/045/chrhdb811_04542_0_en.exd";
        const string source = "Could it be!? The Warrior of Darkness!";
        const string target = "¡<Gender>Guerrera de la Oscuridad<GenderElse>Guerrero de la Oscuridad<GenderEnd>!";
        var baseSource = new FakeExdSource()
            .AddPage(exdPath, SyntheticExd.BuildExd([(121u, source)]))
            .AddLayout(sheet, new ExdLayout(4, [0], 1))
            .AddFieldNames(sheet, "Column1");
        var entries = new[] { Approved(sheet, 121u, "Column1", exdPath, source, target) };
        var pipeline = new PatchPipeline(new ListTranslationSource(entries), new FakePatchBackendFactory(baseSource));

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.Ok, result.Outcome);
        Assert.Equal(1, result.Applied);
        Assert.Equal(0, result.Skipped);

        using var archive = ZipFile.OpenRead(result.OutputPath!);
        var patched = ReadEntryBytes(archive, "files/" + exdPath);
        var patchedRaw = ExdRowReader.ReadRawStrings(patched, 4, [0])
            .Single(row => row.RowId == 121u)
            .Raw;
        Assert.Equal(
            "¡<If><Raw>\u0005<Run>Guerrera de la Oscuridad<RunEnd><Run#2>Guerrero de la Oscuridad<RunEnd#2><MacroEnd>!",
            SeStringTreeTokenizer.TokenizeRawText(patchedRaw));
    }

    [Fact]
    public void Run_WithDebugLogging_EmitsBroadcastDiagnostics()
    {
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new FakePatchBackendFactory(BuildSource()));
        var events = new List<PipelineEvent>();

        var result = pipeline.Run(
            Request(debugLogging: true),
            new SyncProgress<PipelineEvent>(events.Add),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains(events, e =>
            e.Level == PipelineLevel.Debug
            && e.Message.Contains("broadcast", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("duplicados", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_ManyCleanPages_ReportsEveryPatchedPage()
    {
        var entries = new List<TranslationEntry>();
        var source = new FakeExdSource();
        for (var index = 0; index < 120; index++)
        {
            var sheet = $"BulkSheet{index}";
            var path = $"exd/bulksheet_{index}_0_en.exd";
            var english = $"Source {index}";
            entries.Add(Approved(sheet, 1u, string.Empty, path, english, $"Destino {index}"));
            source
                .AddPage(path, SyntheticExd.BuildExd([(1u, english)]))
                .AddLayout(sheet, new ExdLayout(4, [0], 1));
        }

        var events = new List<PipelineEvent>();
        var pipeline = new PatchPipeline(
            new ListTranslationSource(entries),
            new FakePatchBackendFactory(source));

        var result = pipeline.Run(
            Request(),
            new SyncProgress<PipelineEvent>(events.Add),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(120, result.Statistics.PatchedPages);
        Assert.Equal(120, events.Count(item =>
            item.Level == PipelineLevel.Ok
            && item.Message.StartsWith("BulkSheet", StringComparison.Ordinal)));
    }

    [Fact]
    public void Run_ManyMissingPages_ReportsEveryOmissionAndCompleteStatistics()
    {
        var entries = ApprovedManifest().ToList();
        var source = BuildSource();
        for (var index = 0; index < 100; index++)
        {
            var sheet = $"RemovedSheet{index}";
            var path = $"exd/removedsheet_{index}_0_en.exd";
            entries.Add(Approved(sheet, 1u, string.Empty, path, "Removed", "Eliminado"));
            source.AddLayout(sheet, new ExdLayout(4, [0], 1));
        }

        var events = new List<PipelineEvent>();
        var pipeline = new PatchPipeline(
            new ListTranslationSource(entries),
            new FakePatchBackendFactory(source));

        var result = pipeline.Run(
            Request(compatibilityMode: PatchCompatibilityMode.BestEffortVersionMismatch),
            new SyncProgress<PipelineEvent>(events.Add),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.PackagedWithMisses, result.Outcome);
        Assert.Equal(100, result.Statistics.MissingPages);
        Assert.Equal(100, events.Count(item =>
            item.Message.StartsWith("omitida página", StringComparison.Ordinal)));
        Assert.Contains(events, item =>
            item.Message.Contains("100 página(s) ausente(s)", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_WhenPageHasMiss_WarningIncludesMissedRowIds()
    {
        var entries = ApprovedManifest()
            .Append(Approved(1u, "Source not in row", "Fuente ausente"))
            .ToList();
        var pipeline = new PatchPipeline(new ListTranslationSource(entries), new FakePatchBackendFactory(BuildSource()));
        var events = new List<PipelineEvent>();

        var result = pipeline.Run(
            Request(),
            new SyncProgress<PipelineEvent>(events.Add),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.PackagedWithMisses, result.Outcome);
        Assert.Contains(events, e =>
            e.Level == PipelineLevel.Warning
            && e.Message.Contains("Addon", StringComparison.Ordinal)
            && e.Message.Contains("rowId(s): 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_WithSelectedCategoryThatHasNoEntries_PackagesNothing()
    {
        // Addon maps to the "interfaz" domain; selecting only "items" leaves no candidates.
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new FakePatchBackendFactory(BuildSource()));

        var result = pipeline.Run(
            Request(categories: ["items"]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(PatchOutcome.NothingToPackage, result.Outcome);
        Assert.False(File.Exists(Path.Combine(_temp, "out.pmp")));
    }

    [Fact]
    public void Run_WithMatchingCategory_PackagesNormally()
    {
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new FakePatchBackendFactory(BuildSource()));

        var result = pipeline.Run(
            Request(categories: ["interfaz"]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(4, result.Applied);
    }

    [Fact]
    public void Run_AddsItemNameAliasFromSingular_WhenVanillaTextMatches()
    {
        var entries = new[]
        {
            Approved("Item", 9553u, "Singular", ItemExdPath, "Shiva's Diamond Bow", "Arco de diamante de Shiva"),
        };
        var pipeline = new PatchPipeline(new ListTranslationSource(entries), new FakePatchBackendFactory(BuildItemSource()));

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.Ok, result.Outcome);
        Assert.Equal(2, result.Applied);

        using var archive = ZipFile.OpenRead(result.OutputPath!);
        var patched = ReadEntryBytes(archive, "files/exd/item_9500_en.exd");
        var fields = new[] { "Singular", "Plural", "Name" };
        var values = ExdRowReader.ReadRawStrings(patched, 12, [0, 4, 8])
            .Where(row => row.RowId == 9553)
            .ToDictionary(
                row => fields[row.ColumnOrdinal],
                row => Encoding.UTF8.GetString(row.Raw));

        Assert.Equal("Arco de diamante de Shiva", values["Singular"]);
        Assert.Equal("Shiva's Diamond Bows", values["Plural"]);
        Assert.Equal("Arco de diamante de Shiva", values["Name"]);
    }

    [Fact]
    public void Run_SkipsUnsafeSeStringRows_AndPackagesRest()
    {
        var entries = ApprovedManifest()
            .Append(Approved(999u, "Legacy <PayloadFF> source", "Fuente <PayloadFF> legacy"))
            .ToList();
        var pipeline = new PatchPipeline(new ListTranslationSource(entries), new FakePatchBackendFactory(BuildSource()));
        var events = new List<PipelineEvent>();

        var result = pipeline.Run(
            Request(),
            new SyncProgress<PipelineEvent>(events.Add),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.PackagedWithMisses, result.Outcome);
        Assert.Equal(4, result.Applied);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Statistics.UnsafeSeStringEntries);
        Assert.Contains(events, e =>
            e.Level == PipelineLevel.Warning
            && e.Message.Contains("SeString gate", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("omitida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_WhenGameDataCannotBeOpened_ReturnsGameDataError()
    {
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new ThrowingBackendFactory());

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PatchOutcome.GameDataError, result.Outcome);
        Assert.False(result.Success);
    }

    [Fact]
    public void Run_WhenSheetDoesNotExist_PackagesReadableSheetsAndReportsCoverage()
    {
        var entries = ApprovedManifest()
            .Append(Approved(
                "RemovedSheet",
                1u,
                "Text",
                "exd/removedsheet_0_en.exd",
                "Old text",
                "Texto antiguo"))
            .ToList();
        var pipeline = new PatchPipeline(
            new ListTranslationSource(entries),
            new FakePatchBackendFactory(BuildSource()));

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.PackagedWithMisses, result.Outcome);
        Assert.Equal(1, result.Statistics.MissingSheets);
        Assert.Equal(1, result.Statistics.MissingSheetEntries);
        Assert.Equal(4, result.Applied);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public void Run_WhenPageDoesNotExist_PackagesReadablePagesAndReportsCoverage()
    {
        var missingPath = "exd/removedpage_0_en.exd";
        var entries = ApprovedManifest()
            .Append(Approved("RemovedPage", 8u, "Text", missingPath, "Old text", "Texto antiguo"))
            .ToList();
        var source = BuildSource()
            .AddLayout("RemovedPage", new ExdLayout(4, [0], 1));
        var pipeline = new PatchPipeline(
            new ListTranslationSource(entries),
            new FakePatchBackendFactory(source));

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.PackagedWithMisses, result.Outcome);
        Assert.Equal(1, result.Statistics.MissingPages);
        Assert.Equal(1, result.Statistics.MissingPageEntries);
        Assert.Equal(1, result.Statistics.SkippedPages);
        Assert.Equal(4, result.Applied);
    }

    [Fact]
    public void Run_WhenPageVariantIsUnsupported_CountsEveryAffectedEntry()
    {
        const string unsupportedPath = "exd/subrow_0_en.exd";
        var entries = ApprovedManifest()
            .Append(Approved("Subrow", 8u, "Text", unsupportedPath, "Old text", "Texto antiguo"))
            .Append(Approved("Subrow", 9u, "Text", unsupportedPath, "Other text", "Otro texto"))
            .ToList();
        var source = BuildSource()
            .AddPage(unsupportedPath, SyntheticExd.BuildExd([(8u, "Old text"), (9u, "Other text")]))
            .AddLayout("Subrow", new ExdLayout(4, [0], 2));
        var pipeline = new PatchPipeline(
            new ListTranslationSource(entries),
            new FakePatchBackendFactory(source));

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.PackagedWithMisses, result.Outcome);
        Assert.Equal(1, result.Statistics.UnsupportedPages);
        Assert.Equal(2, result.Statistics.UnsupportedPageEntries);
        Assert.Equal(2, result.Statistics.SkippedEntries);
    }

    [Fact]
    public void Run_LowMatchRate_IsFatalInStrictMode()
    {
        var entries = DriftedManifest();
        var pipeline = new PatchPipeline(
            new ListTranslationSource(entries),
            new FakePatchBackendFactory(BuildSingleRowSource()));

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(PatchOutcome.Contaminated, result.Outcome);
        Assert.Equal(1, result.Applied);
        Assert.Equal(60, result.Missed);
        Assert.False(File.Exists(Path.Combine(_temp, "out.pmp")));
    }

    [Fact]
    public void Run_LowMatchRate_ContinuesAfterExplicitVersionMismatchConfirmation()
    {
        var entries = DriftedManifest();
        var pipeline = new PatchPipeline(
            new ListTranslationSource(entries),
            new FakePatchBackendFactory(BuildSingleRowSource()));

        var result = pipeline.Run(
            Request(compatibilityMode: PatchCompatibilityMode.BestEffortVersionMismatch),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.PackagedWithMisses, result.Outcome);
        Assert.Equal(1, result.Applied);
        Assert.Equal(60, result.Missed);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public void Run_WhenNothingCanBeApplied_DoesNotPublishEmptyPackage()
    {
        var entries = new[] { Approved(1u, "Not present", "No presente") };
        var pipeline = new PatchPipeline(
            new ListTranslationSource(entries),
            new FakePatchBackendFactory(BuildSource()));

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(PatchOutcome.NothingToPackage, result.Outcome);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Missed);
        Assert.False(File.Exists(Path.Combine(_temp, "out.pmp")));
    }

    [Fact]
    public void Run_WhenIntegrityFails_PreservesPreviousPackageAndCleansTemporaryFiles()
    {
        var output = Path.Combine(_temp, "out.pmp");
        var previous = Encoding.UTF8.GetBytes("previous verified package");
        File.WriteAllBytes(output, previous);
        var verifier = new FailingVerifier();
        var pipeline = new PatchPipeline(
            new ListTranslationSource(ApprovedManifest()),
            new FakePatchBackendFactory(BuildSource()),
            verifier);

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PatchOutcome.ValidationFailed, result.Outcome);
        Assert.False(result.Success);
        Assert.True(verifier.WasCalled);
        Assert.Equal(previous, File.ReadAllBytes(output));
        Assert.Empty(Directory.EnumerateFiles(_temp, ".*.tmp", SearchOption.TopDirectoryOnly));
        var staging = Path.Combine(_temp, "staging");
        Assert.False(Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any());
    }

    [Fact]
    public void Run_WhenIntegrityPasses_AtomicallyReplacesPreviousPackage()
    {
        var output = Path.Combine(_temp, "out.pmp");
        var previous = Encoding.UTF8.GetBytes("previous verified package");
        File.WriteAllBytes(output, previous);
        var pipeline = new PatchPipeline(
            new ListTranslationSource(ApprovedManifest()),
            new FakePatchBackendFactory(BuildSource()));

        var result = pipeline.Run(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotEqual(previous, File.ReadAllBytes(output));
        using var archive = ZipFile.OpenRead(output);
        Assert.NotNull(archive.GetEntry("meta.json"));
        Assert.Empty(Directory.EnumerateFiles(_temp, ".*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Run_WhenVerifierThrows_ReportsValidationFailureAndPreservesPreviousPackage()
    {
        var output = Path.Combine(_temp, "out.pmp");
        var previous = Encoding.UTF8.GetBytes("previous verified package");
        File.WriteAllBytes(output, previous);
        var events = new List<PipelineEvent>();
        var pipeline = new PatchPipeline(
            new ListTranslationSource(ApprovedManifest()),
            new FakePatchBackendFactory(BuildSource()),
            new ThrowingVerifier());

        var result = pipeline.Run(
            Request(),
            new SyncProgress<PipelineEvent>(events.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(PatchOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(previous, File.ReadAllBytes(output));
        Assert.Contains(events, item =>
            item.Component == PipelineComponent.Verifier
            && item.Level == PipelineLevel.Error
            && item.Message.Contains("verificar", StringComparison.OrdinalIgnoreCase));
    }

    private static FakeExdSource BuildSingleRowSource()
        => new FakeExdSource()
            .AddPage(ExdPath, SyntheticExd.BuildExd([(1u, "Present")]))
            .AddLayout("Addon", new ExdLayout(4, [0], 1));

    private static IReadOnlyList<TranslationEntry> DriftedManifest()
    {
        var entries = new List<TranslationEntry> { Approved(1u, "Present", "Presente") };
        for (var index = 0; index < 60; index++)
        {
            entries.Add(Approved(1u, $"Missing source {index}", $"Fuente ausente {index}"));
        }

        return entries;
    }

    private static string ReadEntryText(ZipArchive archive, string entryName)
        => Encoding.UTF8.GetString(ReadEntryBytes(archive, entryName));

    private static byte[] ReadEntryBytes(ZipArchive archive, string entryName)
    {
        using var stream = archive.GetEntry(entryName)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class ThrowingBackendFactory : IPatchBackendFactory
    {
        public IPatchBackend Open(PatchRequest request)
            => throw new DirectoryNotFoundException("no sqpack");
    }

    private sealed class FailingVerifier : IIntegrityVerifier
    {
        public bool WasCalled { get; private set; }

        public IReadOnlyList<string> Verify(
            string pmpPath,
            IReadOnlyDictionary<string, string> declaredFiles)
        {
            WasCalled = true;
            return ["integrity failure injected by test"];
        }
    }

    private sealed class ThrowingVerifier : IIntegrityVerifier
    {
        public IReadOnlyList<string> Verify(
            string pmpPath,
            IReadOnlyDictionary<string, string> declaredFiles)
            => throw new InvalidOperationException("verifier exception injected by test");
    }
}

/// <summary>Synchronous <see cref="IProgress{T}"/> so tests observe every event inline (the BCL
/// <see cref="Progress{T}"/> marshals callbacks asynchronously, which would race the assertions).</summary>
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
