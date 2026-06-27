using Avalonia.Media;
using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.App.ViewModels;

/// <summary>One rendered console line built from a <see cref="PipelineEvent"/>: a timestamped,
/// component-tagged string colored by severity, matching the mockup's console.</summary>
public sealed class ConsoleLine
{
    public ConsoleLine(PipelineEvent pipelineEvent)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var tag = pipelineEvent.Component == PipelineComponent.Pipeline
            ? string.Empty
            : $"[{pipelineEvent.Component}] ";
        var count = pipelineEvent.Count is { } value ? $" ({value})" : string.Empty;

        Text = $"[{timestamp}] {tag}{pipelineEvent.Message}{count}";
        Foreground = BrushFor(pipelineEvent.Level);
    }

    public string Text { get; }

    public IBrush Foreground { get; }

    private static IBrush BrushFor(PipelineLevel level) => level switch
    {
        PipelineLevel.Debug => new SolidColorBrush(Color.FromRgb(0x7D, 0x86, 0x95)),   // dim gray
        PipelineLevel.Ok => new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),      // green
        PipelineLevel.Warning => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), // amber
        PipelineLevel.Error => new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),   // red
        _ => new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2)),                     // muted gray
    };
}
