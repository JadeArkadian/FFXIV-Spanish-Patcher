using XivSpanish.Translation;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Everything the pipeline needs to build one <c>.pmp</c>. Immutable; the GUI builds it from the
/// game path, the selected categories and the additional options, then hands it to
/// <see cref="PatchPipeline.Run"/>.
/// </summary>
public sealed record PatchRequest
{
    /// <summary>FFXIV install or sqpack path. Null lets the backend auto-detect (XIVLauncher config).</summary>
    public string? GamePath { get; init; }

    /// <summary>
    /// Selected category domains (see <see cref="TranslationCategories"/>). Null means "all
    /// categories": every packageable entry is included. Compared case-insensitively.
    /// </summary>
    public IReadOnlyCollection<string>? Categories { get; init; }

    /// <summary>Only entries with this status are packaged. Defaults to <c>approved</c>.</summary>
    public string Status { get; init; } = TranslationEntryStatus.Approved;

    /// <summary>Output <c>.pmp</c> path.</summary>
    public string OutputPath { get; init; } = DefaultOutputPath();

    /// <summary>Staging directory for the package tree (wiped and rebuilt each run).</summary>
    public string StagingPath { get; init; } = Path.Combine("artifacts", "pmp-staging");

    /// <summary>
    /// Optional vanilla EXD snapshot directory. When set, base bytes/layout come from disk instead
    /// of the live client (reproducible packaging; also the seam tests use to inject a fixture).
    /// </summary>
    public string? BaseExdDir { get; init; }

    /// <summary>Contamination guard threshold (0..1): abort if the content-match rate falls below it.</summary>
    public double MinMatchRate { get; init; } = 0.5;

    /// <summary>Package rows that fail the SeString payload-compatibility gate (loudly logged).</summary>
    public bool ForceSeString { get; init; }

    /// <summary>Run the post-build integrity check ("Verificar integridad al finalizar" toggle).</summary>
    public bool VerifyIntegrity { get; init; } = true;

    /// <summary>Penumbra <c>meta.json</c> fields.</summary>
    public PackageMeta Meta { get; init; } = new();

    /// <summary>Default timestamped output path under <c>artifacts/</c>, matching the mockup naming.</summary>
    public static string DefaultOutputPath()
        => Path.Combine("artifacts", $"FFXIVSpanish-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.pmp");
}

/// <summary>Penumbra mod metadata written to <c>meta.json</c>.</summary>
public sealed record PackageMeta
{
    public string Name { get; init; } = "FFXIVSpanish";
    public string Author { get; init; } = "FFXIVSpanish";
    public string Description { get; init; } = "Traducción al castellano de FFXIV mediante redirección de archivos EXD para Penumbra.";
    public string Version { get; init; } = "0.1.0";
}
