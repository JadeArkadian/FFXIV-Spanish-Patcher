using System.Reflection;

namespace FFXIVSpanishPatcher.App.Services;

public sealed record AppBuildInfo(
    string DisplayVersion,
    AppReleaseVersion? ComparableVersion,
    string PackageVersion,
    string RepositorySlug,
    string LatestReleaseApiUrl,
    string LatestReleasePageUrl)
{
    private const string DefaultPackageVersion = "0.0.0";
    private const string DefaultDisplayVersion = "dev";
    private const string DefaultRepositorySlug = "JadeArkadian/FFXIV-Spanish-Patcher";

    public string WindowTitle => $"FFXIVSpanish Patcher {DisplayVersion}";

    public static AppBuildInfo FromAssembly(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase);

        var rawVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var displayVersion = NormalizeDisplayVersion(rawVersion);
        var comparable = AppReleaseVersion.TryParse(displayVersion, out var parsed)
            ? parsed
            : (AppReleaseVersion?)null;
        var packageVersion = comparable?.ToString() ?? DefaultPackageVersion;

        var repositorySlug = ValueOrDefault(metadata.GetValueOrDefault("RepositorySlug"), DefaultRepositorySlug);
        var apiUrl = ValueOrDefault(
            metadata.GetValueOrDefault("LatestReleaseApiUrl"),
            $"https://api.github.com/repos/{repositorySlug}/releases/latest");
        var pageUrl = ValueOrDefault(
            metadata.GetValueOrDefault("LatestReleasePageUrl"),
            $"https://github.com/{repositorySlug}/releases/latest");

        return new AppBuildInfo(displayVersion, comparable, packageVersion, repositorySlug, apiUrl, pageUrl);
    }

    private static string NormalizeDisplayVersion(string? value)
    {
        var candidate = ValueOrDefault(value, DefaultDisplayVersion);
        var metadataStart = candidate.IndexOf('+', StringComparison.Ordinal);
        if (metadataStart >= 0)
        {
            candidate = candidate[..metadataStart];
        }

        return AppReleaseVersion.TryParse(candidate, out var version)
            ? $"v{version}"
            : candidate;
    }

    private static string ValueOrDefault(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public readonly record struct AppReleaseVersion(int Major, int Minor, int Patch)
    : IComparable<AppReleaseVersion>
{
    public static bool TryParse(string? value, out AppReleaseVersion version)
    {
        version = default;
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
            || !TryParsePart(parts[0], out var major)
            || !TryParsePart(parts[1], out var minor)
            || !TryParsePart(parts[2], out var patch))
        {
            return false;
        }

        version = new AppReleaseVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(AppReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    private static bool TryParsePart(string value, out int part)
        => int.TryParse(value, out part) && part is >= 0 and <= 999;
}
