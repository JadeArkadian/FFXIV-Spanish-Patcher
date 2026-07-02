using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XivSpanish.Translation;

[JsonSerializable(typeof(TranslationEntry))]
[JsonSerializable(typeof(List<TranslationEntry>))]
public sealed partial class TranslationJsonContext : JsonSerializerContext
{
    public static TranslationJsonContext Read { get; } = new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    });

    public static TranslationJsonContext JsonlWrite { get; } = new(new JsonSerializerOptions
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
