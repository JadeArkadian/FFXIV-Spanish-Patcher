using System.Collections.Specialized;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FFXIVSpanishPatcher.App.ViewModels;

namespace FFXIVSpanishPatcher.App.Views;

/// <summary>
/// Read-only, virtualized console. The document stores every line, while AvaloniaEdit creates
/// visual elements only for the visible viewport. Selection and copy span the complete log.
/// </summary>
public sealed class ConsoleLogTextBlock : TextEditor
{
    public static readonly StyledProperty<IEnumerable<ConsoleLine>?> LinesProperty =
        AvaloniaProperty.Register<ConsoleLogTextBlock, IEnumerable<ConsoleLine>?>(nameof(Lines));

    private readonly List<ConsoleLine> _renderedLines = [];
    private readonly List<ConsoleLine> _pending = [];
    private INotifyCollectionChanged? _observable;
    private bool _flushQueued;
    private bool _scrollQueued;
    private double _scrollOffsetWhenQueued;

    protected override Type StyleKeyOverride => typeof(TextEditor);

    static ConsoleLogTextBlock()
    {
        LinesProperty.Changed.AddClassHandler<ConsoleLogTextBlock>(
            (control, _) => control.OnLinesChanged(control.Lines));
    }

    public ConsoleLogTextBlock()
    {
        IsReadOnly = true;
        ShowLineNumbers = false;
        WordWrap = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Options.AllowScrollBelowDocument = false;
        Document.UndoStack.SizeLimit = 0;
        TextArea.SelectionBrush = new SolidColorBrush(Color.Parse("#2563EB"));
        TextArea.TextView.LineTransformers.Add(new ConsoleLineColorizer(_renderedLines));
    }

    public IEnumerable<ConsoleLine>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    /// <summary>Flushes a queued batch immediately; useful for deterministic UI tests.</summary>
    public void FlushPendingLines() => FlushPending();

    private void OnLinesChanged(IEnumerable<ConsoleLine>? newValue)
    {
        if (_observable is not null)
        {
            _observable.CollectionChanged -= OnCollectionChanged;
        }

        _observable = newValue as INotifyCollectionChanged;
        if (_observable is not null)
        {
            _observable.CollectionChanged += OnCollectionChanged;
        }

        _pending.Clear();
        Rebuild(newValue ?? []);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null)
        {
            foreach (var item in args.NewItems.OfType<ConsoleLine>())
            {
                _pending.Add(item);
            }

            QueueFlush();
            return;
        }

        _pending.Clear();
        Rebuild(Lines ?? []);
    }

    private void QueueFlush()
    {
        if (_flushQueued)
        {
            return;
        }

        _flushQueued = true;
        Dispatcher.UIThread.Post(FlushPending, DispatcherPriority.Background);
    }

    private void FlushPending()
    {
        _flushQueued = false;
        if (_pending.Count == 0)
        {
            return;
        }

        var selectionStart = SelectionStart;
        var selectionLength = SelectionLength;
        var wasAtBottom = IsAtBottom();
        var batch = _pending.ToArray();
        _pending.Clear();
        _renderedLines.AddRange(batch);

        Document.Insert(Document.TextLength, BuildText(batch));

        if (selectionLength > 0)
        {
            Select(selectionStart, selectionLength);
        }
        else if (wasAtBottom)
        {
            QueueScrollToEnd();
        }
    }

    private void Rebuild(IEnumerable<ConsoleLine> lines)
    {
        var selectionStart = SelectionStart;
        var selectionLength = SelectionLength;
        var snapshot = lines as IReadOnlyCollection<ConsoleLine> ?? lines.ToArray();

        _renderedLines.Clear();
        _renderedLines.AddRange(snapshot);
        Document.Text = BuildText(snapshot);
        Document.UndoStack.ClearAll();

        if (selectionLength > 0)
        {
            var safeStart = Math.Min(selectionStart, Document.TextLength);
            var safeLength = Math.Min(selectionLength, Document.TextLength - safeStart);
            Select(safeStart, safeLength);
        }
    }

    private bool IsAtBottom()
        => ExtentHeight <= ViewportHeight
           || ExtentHeight - ViewportHeight - VerticalOffset <= 24;

    private void QueueScrollToEnd()
    {
        _scrollOffsetWhenQueued = VerticalOffset;
        if (_scrollQueued)
        {
            return;
        }

        _scrollQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _scrollQueued = false;
                if (SelectionLength == 0 && VerticalOffset + 1 >= _scrollOffsetWhenQueued)
                {
                    TextArea.TextView.EnsureVisualLines();
                    ScrollVerticallyToEnd();
                }
            },
            DispatcherPriority.Loaded);
    }

    private void ScrollVerticallyToEnd()
    {
        ApplyTemplate();
        var scrollViewer = this
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault(candidate => candidate.Name == "PART_ScrollViewer");
        if (scrollViewer is null)
        {
            return;
        }

        var bottom = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, bottom);
    }

    private static string BuildText(IEnumerable<ConsoleLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line.Text).Append('\n');
        }

        return builder.ToString();
    }

    private sealed class ConsoleLineColorizer(IReadOnlyList<ConsoleLine> lines)
        : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine documentLine)
        {
            var index = documentLine.LineNumber - 1;
            if ((uint)index >= (uint)lines.Count)
            {
                return;
            }

            var line = lines[index];
            var timestampEnd = Math.Min(documentLine.EndOffset, documentLine.Offset + line.TimestampText.Length);
            var componentEnd = Math.Min(documentLine.EndOffset, timestampEnd + line.ComponentText.Length);

            Colorize(documentLine.Offset, timestampEnd, line.TimestampBrush);
            Colorize(timestampEnd, componentEnd, line.ComponentBrush);
            Colorize(componentEnd, documentLine.EndOffset, line.Foreground);
        }

        private void Colorize(int start, int end, IBrush brush)
        {
            if (start < end)
            {
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
            }
        }
    }
}
