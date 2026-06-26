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

    private static TranslationEntry Approved(uint rowId, string source, string target)
        => new()
        {
            Source = source,
            Target = target,
            Status = TranslationEntryStatus.Approved,
            SourceKey = new TranslationSourceKey { Sheet = "Addon", RowId = rowId, Field = string.Empty, ExdPath = ExdPath },
        };

    private static IReadOnlyList<TranslationEntry> ApprovedManifest() =>
    [
        Approved(1u, "Independent Arms Mender", "Armero independiente"),
        Approved(2u, string.Empty, "Texto generado"),
        Approved(262u, "Healing Magic Potency", "Potencia de magia curativa"),
    ];

    private PatchRequest Request(IReadOnlyCollection<string>? categories = null) => new()
    {
        OutputPath = Path.Combine(_temp, "out.pmp"),
        StagingPath = Path.Combine(_temp, "staging"),
        Categories = categories,
        VerifyIntegrity = true,
    };

    [Fact]
    public void Run_PatchesSyntheticExd_AndProducesValidPmp()
    {
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new FakePatchBackendFactory(BuildSource()));
        var events = new List<PipelineEvent>();

        var result = pipeline.Run(Request(), new SyncProgress<PipelineEvent>(events.Add));

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

        // A verifier OK event was emitted (the toggle path ran).
        Assert.Contains(events, e => e.Component == PipelineComponent.Verifier && e.Level == PipelineLevel.Ok);
    }

    [Fact]
    public void Run_WithSelectedCategoryThatHasNoEntries_PackagesNothing()
    {
        // Addon maps to the "interfaz" domain; selecting only "items" leaves no candidates.
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new FakePatchBackendFactory(BuildSource()));

        var result = pipeline.Run(Request(categories: ["items"]));

        Assert.False(result.Success);
        Assert.Equal(PatchOutcome.NothingToPackage, result.Outcome);
        Assert.False(File.Exists(Path.Combine(_temp, "out.pmp")));
    }

    [Fact]
    public void Run_WithMatchingCategory_PackagesNormally()
    {
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new FakePatchBackendFactory(BuildSource()));

        var result = pipeline.Run(Request(categories: ["interfaz"]));

        Assert.True(result.Success);
        Assert.Equal(4, result.Applied);
    }

    [Fact]
    public void Run_SkipsUnsafeSeStringRows_AndPackagesRest()
    {
        var entries = ApprovedManifest()
            .Append(Approved(999u, "Legacy <PayloadFF> source", "Fuente <PayloadFF> legacy"))
            .ToList();
        var pipeline = new PatchPipeline(new ListTranslationSource(entries), new FakePatchBackendFactory(BuildSource()));
        var events = new List<PipelineEvent>();

        var result = pipeline.Run(Request(), new SyncProgress<PipelineEvent>(events.Add));

        Assert.True(result.Success);
        Assert.Equal(PatchOutcome.Ok, result.Outcome);
        Assert.Equal(4, result.Applied);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(events, e =>
            e.Level == PipelineLevel.Warning
            && e.Message.Contains("SeString gate", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("omitida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_WhenGameDataCannotBeOpened_ReturnsGameDataError()
    {
        var pipeline = new PatchPipeline(new ListTranslationSource(ApprovedManifest()), new ThrowingBackendFactory());

        var result = pipeline.Run(Request());

        Assert.Equal(PatchOutcome.GameDataError, result.Outcome);
        Assert.False(result.Success);
    }

    private static string ReadEntryText(ZipArchive archive, string entryName)
    {
        using var stream = archive.GetEntry(entryName)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private sealed class ThrowingBackendFactory : IPatchBackendFactory
    {
        public IPatchBackend Open(PatchRequest request)
            => throw new DirectoryNotFoundException("no sqpack");
    }
}

/// <summary>Synchronous <see cref="IProgress{T}"/> so tests observe every event inline (the BCL
/// <see cref="Progress{T}"/> marshals callbacks asynchronously, which would race the assertions).</summary>
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
