using System.Text.Json.Serialization;

namespace FFXIVSpanishPatcher.Pipeline;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PackageModMetaV4))]
internal sealed partial class PipelineJsonContext : JsonSerializerContext
{
}
