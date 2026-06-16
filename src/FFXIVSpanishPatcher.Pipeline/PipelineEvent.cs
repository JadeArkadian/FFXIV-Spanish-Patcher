namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>Which internal component a <see cref="PipelineEvent"/> originates from. Mirrors the
/// console prefixes shown in the GUI ([Extractor], [Patcher], [Packager], ...).</summary>
public enum PipelineComponent
{
    Pipeline,
    Extractor,
    Patcher,
    Packager,
    Verifier,
}

/// <summary>Severity / styling hint for a <see cref="PipelineEvent"/>. The GUI colors the
/// console line accordingly.</summary>
public enum PipelineLevel
{
    Info,
    Ok,
    Warning,
    Error,
}

/// <summary>
/// A single progress report emitted while the pipeline runs. The GUI subscribes via
/// <see cref="System.IProgress{T}"/> and renders one console line per event; <see cref="Count"/>
/// feeds the "OK (255)" suffix the mockup shows next to each patched category.
/// </summary>
public sealed record PipelineEvent(
    PipelineComponent Component,
    string Message,
    PipelineLevel Level = PipelineLevel.Info,
    int? Count = null);
