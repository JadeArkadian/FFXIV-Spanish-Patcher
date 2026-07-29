using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FFXIVSpanishPatcher.App.Views;

internal static class MarkdownAvaloniaRenderer
{
    private static readonly IBrush Text = Brush.Parse("#D7E3F2");
    private static readonly IBrush Muted = Brush.Parse("#91A4BF");
    private static readonly IBrush Accent = Brush.Parse("#69D8FF");
    private static readonly IBrush Border = Brush.Parse("#223555");
    private static readonly IBrush CodeBackground = Brush.Parse("#081321");

    public static Control Render(MarkdownDocument document)
    {
        var panel = new StackPanel { Spacing = 10 };
        AddBlocks(panel.Children, document);
        return panel;
    }

    private static void AddBlocks(Controls controls, ContainerBlock container)
    {
        foreach (var block in container)
        {
            controls.Add(RenderBlock(block));
        }
    }

    private static Control RenderBlock(Block block) => block switch
    {
        HeadingBlock heading => InlineText(
            heading.Inline,
            heading.Level switch { 1 => 23, 2 => 18, 3 => 15, _ => 13 },
            FontWeight.SemiBold),
        ParagraphBlock paragraph => InlineText(paragraph.Inline, 13, FontWeight.Normal),
        ListBlock list => RenderList(list),
        QuoteBlock quote => RenderQuote(quote),
        FencedCodeBlock fenced => RenderCode(fenced),
        CodeBlock code => RenderCode(code),
        ThematicBreakBlock => new Separator { Background = Border, Margin = new Thickness(0, 6) },
        Table table => RenderTable(table),
        _ => throw new InvalidDataException($"Bloque Markdown no renderizable: {block.GetType().Name}."),
    };

    private static SelectableTextBlock InlineText(
        ContainerInline? source,
        double fontSize,
        FontWeight weight)
    {
        var text = new SelectableTextBlock
        {
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = Text,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = fontSize * 1.45,
        };
        text.Inlines = new InlineCollection();
        AddInlines(text.Inlines, source, InlineStyle.None);
        return text;
    }

    private static void AddInlines(
        InlineCollection target,
        ContainerInline? source,
        InlineStyle inherited)
    {
        if (source is null)
        {
            return;
        }

        for (var inline = source.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(StyledRun(literal.Content.ToString(), inherited));
                    break;
                case CodeInline code:
                    var codeRun = StyledRun(code.Content, inherited);
                    codeRun.FontFamily = FontFamily.Parse("Cascadia Mono,Consolas,monospace");
                    codeRun.Foreground = Accent;
                    target.Add(codeRun);
                    break;
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case EmphasisInline emphasis:
                    var style = inherited;
                    if (emphasis.DelimiterChar == '~')
                    {
                        style |= InlineStyle.Strike;
                    }
                    else if (emphasis.DelimiterCount >= 2)
                    {
                        style |= InlineStyle.Bold;
                    }
                    else
                    {
                        style |= InlineStyle.Italic;
                    }

                    AddInlines(target, emphasis, style);
                    break;
                case LinkInline link:
                    target.Add(LinkButton(PlainText(link), link.Url!));
                    break;
                case AutolinkInline autoLink:
                    target.Add(LinkButton(autoLink.Url, autoLink.Url));
                    break;
            }
        }
    }

    private static Run StyledRun(string value, InlineStyle style)
    {
        var run = new Run(value);
        if (style.HasFlag(InlineStyle.Bold))
        {
            run.FontWeight = FontWeight.SemiBold;
        }

        if (style.HasFlag(InlineStyle.Italic))
        {
            run.FontStyle = FontStyle.Italic;
        }

        if (style.HasFlag(InlineStyle.Strike))
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }

        return run;
    }

    private static HyperlinkButton LinkButton(string label, string url)
        => new()
        {
            Content = label,
            NavigateUri = new Uri(url),
            Foreground = Accent,
            Padding = new Thickness(1, 0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private static string PlainText(ContainerInline container)
    {
        var builder = new System.Text.StringBuilder();
        AppendPlainText(builder, container);
        return builder.ToString();
    }

    private static void AppendPlainText(System.Text.StringBuilder builder, ContainerInline container)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content);
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case ContainerInline nested:
                    AppendPlainText(builder, nested);
                    break;
            }
        }
    }

    private static Control RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 6 };
        var index = 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("24,*"),
            };
            grid.Children.Add(new TextBlock
            {
                Text = list.IsOrdered ? $"{index}." : "•",
                Foreground = Accent,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            var content = new StackPanel { Spacing = 5 };
            Grid.SetColumn(content, 1);
            AddBlocks(content.Children, item);
            grid.Children.Add(content);
            panel.Children.Add(grid);
            index++;
        }

        return panel;
    }

    private static Control RenderQuote(QuoteBlock quote)
    {
        var panel = new StackPanel { Spacing = 6 };
        AddBlocks(panel.Children, quote);
        return new Border
        {
            BorderBrush = Accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 5),
            Background = Brush.Parse("#101F35"),
            Child = panel,
        };
    }

    private static Control RenderCode(CodeBlock code)
        => new Border
        {
            Background = CodeBackground,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new SelectableTextBlock
            {
                Text = code.Lines.ToString(),
                FontFamily = FontFamily.Parse("Cascadia Mono,Consolas,monospace"),
                FontSize = 12,
                Foreground = Accent,
                TextWrapping = TextWrapping.Wrap,
            },
        };

    private static Control RenderTable(Table table)
    {
        var rows = table.OfType<TableRow>().ToArray();
        var columnCount = rows.Select(row => row.Count).DefaultIfEmpty(0).Max();
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                string.Join(",", Enumerable.Repeat("*", columnCount))),
        };

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = rows[rowIndex].OfType<TableCell>().ToArray();
            for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
            {
                var content = new StackPanel { Spacing = 3 };
                AddBlocks(content.Children, cells[columnIndex]);
                var border = new Border
                {
                    BorderBrush = Border,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(8, 6),
                    Background = rows[rowIndex].IsHeader ? Brush.Parse("#111F35") : Brushes.Transparent,
                    Child = content,
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex);
                grid.Children.Add(border);
            }
        }

        return grid;
    }

    [Flags]
    private enum InlineStyle
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Strike = 4,
    }
}
