using System.Reflection;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FFXIVSpanishPatcher.App.Services;

public sealed class TranslationMilestoneService
{
    public const string ResourceName = "FFXIVSpanishPatcher.App.translation-milestones.md";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UsePipeTables()
        .Build();

    public MarkdownDocument Load(Assembly? assembly = null)
    {
        assembly ??= typeof(TranslationMilestoneService).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"No existe el recurso {ResourceName}.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public MarkdownDocument LoadOrFallback(Assembly? assembly = null)
    {
        try
        {
            return Load(assembly);
        }
        catch
        {
            return Parse(
                "# Hitos de traducción\n\n" +
                "No se ha podido mostrar el historial incluido en esta compilación.\n");
        }
    }

    public MarkdownDocument Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidDataException("El historial de traducción está vacío.");
        }

        var document = Markdown.Parse(markdown, Pipeline);
        ValidateBlocks(document);
        return document;
    }

    private static void ValidateBlocks(ContainerBlock container)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    ValidateInlineContainer(heading.Inline);
                    break;
                case ParagraphBlock paragraph:
                    ValidateInlineContainer(paragraph.Inline);
                    break;
                case FencedCodeBlock:
                case CodeBlock:
                case ThematicBreakBlock:
                    break;
                case ListBlock list:
                    ValidateBlocks(list);
                    break;
                case ListItemBlock item:
                    ValidateBlocks(item);
                    break;
                case QuoteBlock quote:
                    ValidateBlocks(quote);
                    break;
                case Table table:
                    ValidateBlocks(table);
                    break;
                case TableRow row:
                    ValidateBlocks(row);
                    break;
                case TableCell cell:
                    ValidateBlocks(cell);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Bloque Markdown no compatible: {block.GetType().Name}.");
            }
        }
    }

    private static void ValidateInlineContainer(ContainerInline? container)
    {
        if (container is null)
        {
            return;
        }

        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline:
                case CodeInline:
                case LineBreakInline:
                    break;
                case EmphasisInline emphasis:
                    ValidateInlineContainer(emphasis);
                    break;
                case LinkInline link:
                    if (link.IsImage)
                    {
                        throw new InvalidDataException("Las imágenes Markdown no son compatibles.");
                    }

                    ValidateSafeHttpUri(link.Url);
                    ValidateInlineContainer(link);
                    break;
                case AutolinkInline autoLink:
                    ValidateSafeHttpUri(autoLink.Url);
                    break;
                case HtmlInline:
                    throw new InvalidDataException("El HTML incrustado no está permitido.");
                default:
                    throw new InvalidDataException(
                        $"Elemento Markdown no compatible: {inline.GetType().Name}.");
            }
        }
    }

    private static void ValidateSafeHttpUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException($"Enlace Markdown no permitido: {value}");
        }
    }
}
