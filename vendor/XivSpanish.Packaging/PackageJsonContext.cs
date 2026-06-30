using System.Text.Json.Serialization;

namespace XivSpanish.Packager;

[JsonSerializable(typeof(string[]))]
internal sealed partial class PackageJsonContext : JsonSerializerContext
{
}
