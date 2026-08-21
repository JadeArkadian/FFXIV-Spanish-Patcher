namespace FFXIVSpanishPatcher.Plugin;

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
