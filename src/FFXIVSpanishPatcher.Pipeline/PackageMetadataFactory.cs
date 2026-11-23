using System.Globalization;
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

    /// <param name="generatedAt">
    /// When the mod was generated, in the user's local time. It is written as the user's wall
    /// clock read it, with a fixed day/month/year layout that does not depend on the system culture.
    /// </param>
    /// <remarks>
    /// The description states what was generated and when. It does not list the categories: every
    /// package contains all of them, and Penumbra already shows them as the mod's options.
    /// </remarks>
    public static PackageMeta Create(
        string packageVersion,
        string? installedGameVersion,
        DateTimeOffset generatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        var version = $"v{packageVersion}"
                      + (string.IsNullOrEmpty(installedGameVersion) ? "" : $"-{installedGameVersion}");
        var generated = generatedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var description = new PackageMeta().Description
                          + $"\n\nVersión del patcher: v{packageVersion}"
                          + $"\nVersión de FFXIV: {installedGameVersion ?? "desconocida"}"
                          + $"\nFecha de generación: {generated}";
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
