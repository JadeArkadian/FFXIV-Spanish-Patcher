using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Stable, presentation-independent identity carried by every generated mod tree. It is separate
/// from <c>meta.json</c> because Penumbra users may rename or otherwise customize that metadata.
/// </summary>
public static class TranslationModIdentity
{
    public const string MarkerFileName = "ffxivspanish.identity.json";
    public const int SchemaVersion = 1;
    private const string Product = "FFXIVSpanish";
    private const string CanonicalPayload = "ffxivspanish/mod-identity/v1|penumbra-v4|categorized-exd";

    /// <summary>Hash of the fixed identity contract, not of user-editable metadata or EXD bytes.</summary>
    public static string Fingerprint { get; } = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalPayload)));

    public static void Write(string treePath)
    {
        var markerPath = Path.Combine(treePath, MarkerFileName);
        var json = "{\"SchemaVersion\":" + SchemaVersion
                   + ",\"Product\":\"" + Product
                   + "\",\"Fingerprint\":\"" + Fingerprint + "\"}";
        File.WriteAllText(markerPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Validates a marker without using mutable display fields from <c>meta.json</c>.</summary>
    public static bool HasValidMarker(string treePath)
    {
        var markerPath = Path.Combine(treePath, MarkerFileName);
        if (!File.Exists(markerPath) || new FileInfo(markerPath).LinkTarget is not null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(markerPath));
            var root = document.RootElement;
            return root.TryGetProperty("SchemaVersion", out var schemaVersion)
                   && schemaVersion.TryGetInt32(out var schema)
                   && schema == SchemaVersion
                   && root.TryGetProperty("Product", out var product)
                   && StringComparer.Ordinal.Equals(product.GetString(), Product)
                   && root.TryGetProperty("Fingerprint", out var fingerprint)
                   && StringComparer.Ordinal.Equals(fingerprint.GetString(), Fingerprint);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
