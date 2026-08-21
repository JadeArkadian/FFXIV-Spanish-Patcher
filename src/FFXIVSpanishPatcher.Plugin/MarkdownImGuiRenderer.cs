using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FFXIVSpanishPatcher.Plugin;

internal static class MarkdownImGuiRenderer
{
    private static readonly Vector4 Text = new(0.937f, 0.961f, 0.992f, 1f);
    private static readonly Vector4 Muted = new(0.557f, 0.643f, 0.749f, 1f);
    private static readonly Vector4 Cyan = new(0.420f, 0.847f, 1f, 1f);
    private static readonly Vector4 Gold = new(0.941f, 0.741f, 0.345f, 1f);

    public static void Draw(MarkdownDocument document)
    {
        var tableIndex = 0;
        DrawBlocks(document, ref tableIndex, skipLeadingTitle: true);
    }

    private static void DrawBlocks(
        ContainerBlock container,
        ref int tableIndex,
        bool skipLeadingTitle = false)
    {
        var firstBlock = true;
        foreach (var block in container)
        {
            if (firstBlock && skipLeadingTitle && block is HeadingBlock { Level: 1 })
            {
                firstBlock = false;
                continue;
            }

            firstBlock = false;
            switch (block)
            {
                case HeadingBlock heading:
                    DrawHeading(heading);
                    break;
                case ParagraphBlock paragraph:
                    DrawInline(paragraph.Inline);
                    ImGui.Spacing();
                    break;
                case ListBlock list:
                    DrawList(list, ref tableIndex);
                    ImGui.Spacing();
                    break;
                case QuoteBlock quote:
                    DrawQuote(quote, ref tableIndex);
                    ImGui.Spacing();
                    break;
                case Table table:
                    DrawTable(table, tableIndex++);
                    ImGui.Spacing();
                    break;
                case FencedCodeBlock fenced:
                    DrawCode(fenced);
                    break;
                case CodeBlock code:
                    DrawCode(code);
                    break;
                case ThematicBreakBlock:
                    ImGui.Separator();
                    ImGui.Spacing();
                    break;
            }
        }
    }

    private static void DrawHeading(HeadingBlock heading)
    {
        var color = heading.Level switch
        {
            1 => Gold,
            2 => Cyan,
            _ => Text,
        };
        var label = PlainText(heading.Inline);
        ImGui.Spacing();
        ImGui.TextColored(color, heading.Level == 1 ? label.ToUpperInvariant() : label);
        if (heading.Level <= 2)
        {
            ImGui.Separator();
        }
        ImGui.Spacing();
    }

    private static void DrawList(ListBlock list, ref int tableIndex)
    {
        var index = 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = list.IsOrdered ? $"{index}." : "•";
            ImGui.TextColored(Cyan, marker);
            ImGui.SameLine();
            var first = true;
            foreach (var block in item)
            {
                if (block is ParagraphBlock paragraph)
                {
                    if (!first)
                    {
                        ImGui.Indent(18f);
                    }
                    DrawInline(paragraph.Inline);
                    if (!first)
                    {
                        ImGui.Unindent(18f);
                    }
                }
                else if (block is ContainerBlock nested)
                {
                    ImGui.Indent(18f);
                    DrawBlocks(nested, ref tableIndex);
                    ImGui.Unindent(18f);
                }
                first = false;
            }
            index++;
        }
    }

    private static void DrawQuote(QuoteBlock quote, ref int tableIndex)
    {
        ImGui.TextColored(Cyan, "▌");
        ImGui.SameLine();
        ImGui.BeginGroup();
        DrawBlocks(quote, ref tableIndex);
        ImGui.EndGroup();
    }

    private static void DrawCode(CodeBlock code)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Cyan);
        ImGui.TextWrapped(code.Lines.ToString());
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static void DrawTable(Table table, int tableIndex)
    {
        var rows = table.OfType<TableRow>().ToArray();
        var columnCount = rows.Select(row => row.Count).DefaultIfEmpty(0).Max();
        if (columnCount == 0
            || !ImGui.BeginTable(
                $"##markdown-table-{tableIndex}",
                columnCount,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            foreach (var cell in row.OfType<TableCell>())
            {
                ImGui.TableNextColumn();
                if (row.IsHeader)
                {
                    ImGui.TextColored(Cyan, PlainText(cell));
                }
                else
                {
                    DrawCell(cell);
                }
            }
        }

        ImGui.EndTable();
    }

    private static void DrawCell(TableCell cell)
    {
        foreach (var block in cell)
        {
            if (block is ParagraphBlock paragraph)
            {
                DrawInline(paragraph.Inline);
            }
            else
            {
                ImGui.TextWrapped(PlainText(block));
            }
        }
    }

    private static void DrawInline(ContainerInline? source)
    {
        var segments = new List<InlineSegment>();
        AppendSegments(segments, source, InlineStyle.None, null);
        var originX = ImGui.GetCursorScreenPos().X;
        var rightX = originX + ImGui.GetContentRegionAvail().X;
        var currentX = originX;
        var hasContent = false;

        foreach (var segment in segments)
        {
            foreach (var token in Tokenize(segment.Text))
            {
                if (token == "\n")
                {
                    ImGui.NewLine();
                    currentX = originX;
                    hasContent = false;
                    continue;
                }

                var width = ImGui.CalcTextSize(token).X;
                if (hasContent && currentX + width > rightX)
                {
                    ImGui.NewLine();
                    currentX = originX;
                    hasContent = false;
                }

                DrawToken(token, segment.Style, segment.Url);
                currentX += width;
                hasContent = true;
                ImGui.SameLine(0f, 0f);
            }
        }

        ImGui.NewLine();
    }

    private static void DrawToken(string text, InlineStyle style, string? url)
    {
        var color = style.HasFlag(InlineStyle.Link)
            ? Cyan
            : style.HasFlag(InlineStyle.Bold)
                ? Gold
                : style.HasFlag(InlineStyle.Code)
                    ? Cyan
                    : style.HasFlag(InlineStyle.Italic) || style.HasFlag(InlineStyle.Strike)
                    ? Muted
                    : Text;
        ImGui.TextColored(color, text);
        if (url is null)
        {
            return;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(url);
        }
        if (ImGui.IsItemClicked())
        {
            Util.OpenLink(url);
        }
    }

    private static void AppendSegments(
        ICollection<InlineSegment> target,
        ContainerInline? source,
        InlineStyle inherited,
        string? inheritedUrl)
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
                    target.Add(new InlineSegment(literal.Content.ToString(), inherited, inheritedUrl));
                    break;
                case CodeInline code:
                    target.Add(new InlineSegment(code.Content, inherited | InlineStyle.Code, inheritedUrl));
                    break;
                case LineBreakInline:
                    target.Add(new InlineSegment("\n", inherited, inheritedUrl));
                    break;
                case EmphasisInline emphasis:
                    var style = emphasis.DelimiterChar == '~'
                        ? inherited | InlineStyle.Strike
                        : emphasis.DelimiterCount >= 2
                            ? inherited | InlineStyle.Bold
                            : inherited | InlineStyle.Italic;
                    AppendSegments(target, emphasis, style, inheritedUrl);
                    break;
                case LinkInline link:
                    AppendSegments(target, link, inherited | InlineStyle.Link, link.Url);
                    break;
                case AutolinkInline autoLink:
                    target.Add(new InlineSegment(autoLink.Url, inherited | InlineStyle.Link, autoLink.Url));
                    break;
            }
        }
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var token = new StringBuilder();
        foreach (var character in text)
        {
            if (character == '\n')
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
                yield return "\n";
            }
            else
            {
                token.Append(character);
                if (Char.IsWhiteSpace(character))
                {
                    yield return token.ToString();
                    token.Clear();
                }
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }

    private static string PlainText(ContainerInline? container)
    {
        var builder = new StringBuilder();
        AppendPlainText(builder, container);
        return builder.ToString();
    }

    private static string PlainText(ContainerBlock container)
    {
        var builder = new StringBuilder();
        foreach (var block in container)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    AppendPlainText(builder, paragraph.Inline);
                    break;
                case ContainerBlock nested:
                    builder.Append(PlainText(nested));
                    break;
            }
        }
        return builder.ToString();
    }

    private static string PlainText(Block block)
        => block switch
        {
            ParagraphBlock paragraph => PlainText(paragraph.Inline),
            ContainerBlock container => PlainText(container),
            _ => String.Empty,
        };

    private static void AppendPlainText(StringBuilder builder, ContainerInline? container)
    {
        if (container is null)
        {
            return;
        }

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

    private sealed record InlineSegment(string Text, InlineStyle Style, string? Url);

    [Flags]
    private enum InlineStyle
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Strike = 4,
        Code = 8,
        Link = 16,
    }
}
