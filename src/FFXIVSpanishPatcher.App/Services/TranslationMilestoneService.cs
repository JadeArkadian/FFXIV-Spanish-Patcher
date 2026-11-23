using System.Reflection;
using Markdig.Syntax;
using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.App.Services;

public sealed class TranslationMilestoneService
{
    public const string ResourceName = "FFXIVSpanishPatcher.App.translation-milestones.md";

    private readonly SafeMarkdownParser _parser = new();

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

        return _parser.Parse(markdown);
    }
}
