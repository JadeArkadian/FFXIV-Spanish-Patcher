namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// How a game version that could not be read is written into a fingerprint, and the one test for
/// it. Every caller that must tell "no version" from a real one goes through here.
/// </summary>
public static class GameVersionText
{
    public const string Unknown = "desconocida";

    public static bool IsUnknown(string? value)
        => String.IsNullOrWhiteSpace(value)
            || StringComparer.OrdinalIgnoreCase.Equals(value, Unknown)
            || StringComparer.OrdinalIgnoreCase.Equals(value, "sin datos");
}

/// <summary>Inputs that require rebuilding the managed Penumbra tree after a successful installation.</summary>
public sealed record BuildFingerprint(
    int SchemaVersion,
    string PluginAssemblyVersion,
    string CorpusSha256,
    string RecommendedGameVersion,
    string InstalledGameVersion,
    int CategorySchemaVersion,
    int PackageFormatVersion,
    string ModIdentity,
    string SuccessfulPayloadId,
    DateTimeOffset CompletedAtUtc)
{
    /// <summary>Only deterministic build inputs decide whether regeneration is required.</summary>
    public bool RequiresRegenerationComparedTo(BuildFingerprint? previous)
        => previous is null
            || SchemaVersion != previous.SchemaVersion
            || !StringComparer.Ordinal.Equals(PluginAssemblyVersion, previous.PluginAssemblyVersion)
            || !StringComparer.Ordinal.Equals(CorpusSha256, previous.CorpusSha256)
            || !StringComparer.Ordinal.Equals(RecommendedGameVersion, previous.RecommendedGameVersion)
            || !StringComparer.Ordinal.Equals(InstalledGameVersion, previous.InstalledGameVersion)
            || CategorySchemaVersion != previous.CategorySchemaVersion
            || PackageFormatVersion != previous.PackageFormatVersion
            || !StringComparer.Ordinal.Equals(ModIdentity, previous.ModIdentity);
}
