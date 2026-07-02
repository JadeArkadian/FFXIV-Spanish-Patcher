using System.Text.Json.Serialization;

namespace FFXIVSpanishPatcher.Pipeline;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PackageDefaultMod))]
[JsonSerializable(typeof(PackageModMeta))]
internal sealed partial class PipelineJsonContext : JsonSerializerContext
{
}
