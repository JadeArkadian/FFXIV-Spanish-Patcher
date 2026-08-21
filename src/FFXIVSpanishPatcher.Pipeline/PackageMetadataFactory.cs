using System.Reflection;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Canonical builder for the user-facing metadata in Penumbra's <c>meta.json</c>.
/// Both application frontends use this so a managed tree describes itself exactly like a
/// package produced by the classic patcher.
/// </summary>
public static class PackageMetadataFactory
{
    public const string LandingPageUrl = "https://ffxivspanish.carrd.co/";

    public static PackageMeta Create(
        string packageVersion,
        string? installedGameVersion,
        IEnumerable<string> includedCategoryLabels,
        IEnumerable<string> enabledByDefaultCategoryLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentNullException.ThrowIfNull(includedCategoryLabels);
        ArgumentNullException.ThrowIfNull(enabledByDefaultCategoryLabels);

        var version = $"v{packageVersion}"
                      + (string.IsNullOrEmpty(installedGameVersion) ? "" : $"-{installedGameVersion}");
        var includedDomains = string.Join(
            "\n",
            includedCategoryLabels.Select(label => $"* {label}"));
        var enabledByDefaultDomains = string.Join(
            "\n",
            enabledByDefaultCategoryLabels.Select(label => $"* {label}"));
        var description = new PackageMeta().Description
                          + $"\n\nVersión del patcher: v{packageVersion}"
                          + $"\nVersión de FFXIV: {installedGameVersion ?? "desconocida"}"
                          + $"\n\nCategorías incluidas:\n{includedDomains}"
                          + $"\n\nCategorías activadas en primera importación de Penumbra:\n{enabledByDefaultDomains}";
        return new PackageMeta
        {
            Version = version,
            Description = description,
            Website = LandingPageUrl,
        };
    }

    /// <summary>Returns the three-part release number used by package metadata.</summary>
    public static string PackageVersionFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (TryNormalizeReleaseVersion(informationalVersion, out var packageVersion))
        {
            return packageVersion;
        }

        var assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is null
            ? "0.0.0"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(assemblyVersion.Build, 0)}";
    }

    private static bool TryNormalizeReleaseVersion(string? value, out string packageVersion)
    {
        packageVersion = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["refs/tags/".Length..];
        }

        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
        {
            candidate = candidate[1..];
        }

        var suffixStart = candidate.IndexOfAny(['+', '-']);
        if (suffixStart >= 0)
        {
            candidate = candidate[..suffixStart];
        }

        var parts = candidate.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var patch)
            || major is < 0 or > 999
            || minor is < 0 or > 999
            || patch is < 0 or > 999)
        {
            return false;
        }

        packageVersion = $"{major}.{minor}.{patch}";
        return true;
    }
}
