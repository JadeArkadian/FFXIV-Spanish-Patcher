using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FFXIVSpanishPatcher.App.ViewModels;
using FFXIVSpanishPatcher.App.Views;
using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.App.Tests;

public sealed class ConsoleLogTextBlockTests
{
    [AvaloniaFact]
    public void TenThousandLines_RemainSelectableAndVirtualized()
    {
        var lines = new ObservableCollection<ConsoleLine>();
        var control = new ConsoleLogTextBlock { Lines = lines };
        var window = new Window
        {
            Width = 1000,
            Height = 300,
            Content = control,
        };
        window.Show();
        var stopwatch = Stopwatch.StartNew();

        AddLines(lines, 0, 10_000);
        control.FlushPendingLines();
        Assert.NotNull(window.CaptureRenderedFrame());
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"El render de 10.000 líneas tardó {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.Equal(10_001, control.LineCount);
        control.TextArea.TextView.EnsureVisualLines();
        Assert.InRange(control.TextArea.TextView.VisualLines.Count, 1, 100);
        control.Select(4, lines[0].Text.Length + lines[1].Text.Length);
        var selectedLength = control.SelectionLength;

        lines.Add(new ConsoleLine(new PipelineEvent(PipelineComponent.Verifier, "línea posterior")));
        control.FlushPendingLines();

        Assert.Equal(4, control.SelectionStart);
        Assert.Equal(selectedLength, control.SelectionLength);
        window.Close();
    }

    [AvaloniaFact]
    public void TenThousandRenderedLines_StayResponsiveWhileScrollingAndAppending()
    {
        var lines = new ObservableCollection<ConsoleLine>();
        var control = new ConsoleLogTextBlock { Lines = lines };
        var window = new Window
        {
            Width = 1000,
            Height = 300,
            Content = control,
        };
        window.Show();

        var initial = Stopwatch.StartNew();
        AddLines(lines, 0, 10_000);
        control.FlushPendingLines();
        Assert.NotNull(window.CaptureRenderedFrame());
        control.ApplyTemplate();
        initial.Stop();

        var scroll = Assert.Single(
            control.GetVisualDescendants().OfType<ScrollViewer>(),
            candidate => candidate.Name == "PART_ScrollViewer");
        var interaction = Stopwatch.StartNew();
        for (var index = 0; index < 12; index++)
        {
            var fraction = index % 2 == 0 ? 0.15 : 0.85;
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height * fraction));
            Assert.NotNull(window.CaptureRenderedFrame());
        }

        interaction.Stop();

        var incremental = Stopwatch.StartNew();
        AddLines(lines, 10_000, 100);
        control.FlushPendingLines();
        Assert.NotNull(window.CaptureRenderedFrame());
        incremental.Stop();

        window.Close();
        Assert.True(
            initial.Elapsed < TimeSpan.FromSeconds(2),
            $"El render visible de 10.000 líneas tardó {initial.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.True(
            incremental.Elapsed < TimeSpan.FromMilliseconds(500),
            $"Añadir 100 líneas tras la ejecución tardó {incremental.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.True(
            interaction.Elapsed < TimeSpan.FromMilliseconds(750),
            $"Desplazar la consola tras la ejecución tardó {interaction.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.InRange(control.TextArea.TextView.VisualLines.Count, 1, 100);
    }

    [AvaloniaFact]
    public void NewLines_FollowTheEndUnlessTheUserHasScrolledUp()
    {
        var lines = new ObservableCollection<ConsoleLine>();
        var control = new ConsoleLogTextBlock { Lines = lines };
        var window = new Window
        {
            Width = 900,
            Height = 180,
            Content = control,
        };
        window.Show();

        AddLines(lines, 0, 200);
        control.FlushPendingLines();
        RenderPendingUi(window);
        var scroll = Assert.Single(
            control.GetVisualDescendants().OfType<ScrollViewer>(),
            candidate => candidate.Name == "PART_ScrollViewer");
        scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        RenderPendingUi(window);

        AddLines(lines, 200, 1);
        control.FlushPendingLines();
        RenderPendingUi(window);

        AssertAtBottom(scroll);

        scroll.Offset = new Vector(0, scroll.Extent.Height / 3);
        RenderPendingUi(window);
        var manualOffset = scroll.Offset.Y;
        AddLines(lines, 201, 1);
        control.FlushPendingLines();
        RenderPendingUi(window);

        Assert.InRange(scroll.Offset.Y, manualOffset - 1, manualOffset + 1);
        window.Close();
    }

    [AvaloniaFact]
    public void NewLines_FollowTheEndWithoutMovingHorizontally()
    {
        var lines = new ObservableCollection<ConsoleLine>();
        var control = new ConsoleLogTextBlock { Lines = lines };
        var window = new Window
        {
            Width = 700,
            Height = 180,
            Content = control,
        };
        window.Show();

        AddLongLines(lines, 0, 200);
        control.FlushPendingLines();
        RenderPendingUi(window);
        var scroll = Assert.Single(
            control.GetVisualDescendants().OfType<ScrollViewer>(),
            candidate => candidate.Name == "PART_ScrollViewer");
        Assert.True(
            scroll.Extent.Width > scroll.Viewport.Width,
            $"Se esperaba overflow horizontal: extent={scroll.Extent.Width}, viewport={scroll.Viewport.Width}, "
            + $"offset={scroll.Offset}, líneas={control.LineCount}, visuales="
            + string.Join(",", control.TextArea.TextView.VisualLines.Select(line => line.FirstDocumentLine.LineNumber)));

        scroll.Offset = new Vector(40, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        RenderPendingUi(window);
        var horizontalOffset = scroll.Offset.X;

        AddLongLines(lines, 200, 1);
        control.FlushPendingLines();
        RenderPendingUi(window);

        AssertAtBottom(scroll);
        Assert.InRange(scroll.Offset.X, horizontalOffset - 1, horizontalOffset + 1);
        window.Close();
    }

    private static void RenderPendingUi(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());
    }

    private static void AssertAtBottom(ScrollViewer scroll)
    {
        var expected = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        Assert.InRange(scroll.Offset.Y, expected - 1, expected + 1);
    }

    private static void AddLines(ObservableCollection<ConsoleLine> lines, int start, int count)
    {
        for (var index = start; index < start + count; index++)
        {
            lines.Add(new ConsoleLine(new PipelineEvent(
                PipelineComponent.Patcher,
                $"Página {index}: 150 traducciones aplicadas",
                index % 43 == 0 ? PipelineLevel.Warning : PipelineLevel.Ok)));
        }
    }

    private static void AddLongLines(ObservableCollection<ConsoleLine> lines, int start, int count)
    {
        for (var index = start; index < start + count; index++)
        {
            lines.Add(new ConsoleLine(new PipelineEvent(
                PipelineComponent.Pipeline,
                $"Cobertura: {index:N0} escrituras, 0 misses, 0 hojas ausentes. {new string('x', 240)}",
                PipelineLevel.Ok)));
        }
    }
}
